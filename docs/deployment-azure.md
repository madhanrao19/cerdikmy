# Deployment — Azure (Container Apps + Azure SQL + Blob)

This guide deploys cerdikMY to Azure using the Bicep templates under
`infra/azure/`:

- `main.bicep` — provisions the managed infrastructure.
- `deploy.azcli.sh` — wraps the resource-group creation, `az deployment group`
  call, image build/push, and the one-off migration job.

The managed topology replaces the self-hosted Hostinger pieces with Azure
services:

| Hostinger (self-hosted) | Azure equivalent |
| --- | --- |
| `api` / `web` / `worker` containers (Compose) | **Azure Container Apps** (one app each) |
| SQL Server 2025 container | **Azure SQL Database** |
| MinIO (`STORAGE_PROVIDER=s3`) | **Azure Blob Storage** (`STORAGE_PROVIDER=azure`) |
| nginx + certbot | Container Apps built-in ingress + managed TLS |
| `.env` file | Container App env vars + **Key Vault**-backed secrets |
| Hangfire on SQL Server | unchanged — Hangfire still uses the Azure SQL DB |

> See [architecture.md](architecture.md) for the layered model and
> [deployment-hostinger.md](deployment-hostinger.md) for the self-hosted variant.

---

## 1. Prerequisites

- Azure CLI (`az`) logged in: `az login` and `az account set --subscription <id>`.
- Docker (to build the three images) and a target registry — **Azure Container
  Registry (ACR)**.
- The `containerapp` CLI extension: `az extension add --name containerapp`.

## 2. What `main.bicep` provisions

`infra/azure/main.bicep` declares the resource graph (names are illustrative —
check the template's parameters/outputs for the exact ones):

- **Container Apps Environment** + Log Analytics workspace (OpenTelemetry/Serilog
  sink).
- **Container Apps**: `cerdik-api` (external ingress, target port 8080),
  `cerdik-web` (external ingress, target port 8080), `cerdik-worker` (no ingress).
- **Azure SQL Database** (`cerdikmy`) + logical server with a firewall rule for
  Azure services.
- **Azure Blob Storage** account + the media container.
- **Key Vault** for secrets, with the Container Apps using a managed identity to
  read them.
- **Azure Container Registry** (or reference to an existing one) for the images.

## 3. Build, push, deploy (`deploy.azcli.sh`)

```bash
cd /path/to/cerdikmy

# Authenticate + set subscription
az login
az account set --subscription "<SUBSCRIPTION_ID>"

# Run the wrapper (creates RG, deploys Bicep, builds/pushes images, runs migration)
chmod +x infra/azure/deploy.azcli.sh
./infra/azure/deploy.azcli.sh
```

Under the hood the script:

1. `az group create` for the target resource group + region (e.g.
   `southeastasia`, closest to Malaysia).
2. Builds and pushes the three images to ACR
   (`infra/docker/api.Dockerfile`, `web.Dockerfile`, `worker.Dockerfile`).
3. `az deployment group create --template-file infra/azure/main.bicep` with the
   parameters (image tags, SQL admin login/password, etc.).
4. Runs database migrations as a **one-off job** (see §5).

## 4. Secret + app-setting mapping

The app reads flat env vars (`DATABASE_URL`, `STORAGE_PROVIDER`, `AI_PROVIDER`,
`JWT_*`, …). ASP.NET Core also binds **`Section__Key`** double-underscore env
vars onto `appsettings` configuration sections, so Container App settings map
cleanly. Set them as Container App env vars, with secrets sourced from Key Vault:

| `.env` (Hostinger) | Azure setting | Notes |
| --- | --- | --- |
| `DATABASE_URL` | `DATABASE_URL` → Azure SQL connection string | Key Vault secret; use the Azure SQL server FQDN, `Encrypt=True`. |
| `STORAGE_PROVIDER=s3` | `STORAGE_PROVIDER=azure` | switches `IStorageService` to the Blob adapter. |
| `S3_*` (MinIO) | *(unused)* | replaced by the connection string below. |
| `AZURE_STORAGE_CONNECTION_STRING` | same | Key Vault secret; Blob account connection string. |
| `AI_PROVIDER` + keys | same | `openai` / `azureopenai` / `anthropic`; keys as Key Vault secrets. |
| `JWT_ACCESS_SECRET` / `JWT_REFRESH_SECRET` | same | Key Vault secrets (≥32 chars). |
| `PAYMENT_PROVIDER` + provider keys | same | Key Vault secrets. |
| `OTEL_EXPORTER_OTLP_ENDPOINT` / `OTEL_SERVICE_NAME` | same | point at Log Analytics / an OTLP collector. |
| `API_BASE_URL` (web app) | the `cerdik-api` ingress FQDN | so the Blazor app reaches the API. |

Set the critical storage switch explicitly:

```bash
az containerapp update -g <rg> -n cerdik-api    --set-env-vars STORAGE_PROVIDER=azure
az containerapp update -g <rg> -n cerdik-worker --set-env-vars STORAGE_PROVIDER=azure
```

Secrets should be Container App secret refs backed by Key Vault, e.g.:

```bash
az containerapp secret set -g <rg> -n cerdik-api \
  --secrets db-url=keyvaultref:https://<vault>.vault.azure.net/secrets/DATABASE_URL,identityref:system
az containerapp update -g <rg> -n cerdik-api \
  --set-env-vars DATABASE_URL=secretref:db-url
```

## 5. Running migrations as a one-off job

Do not auto-migrate on every cold start of a scaled-out revision. Instead run a
**Container Apps Job** that invokes the API's `--migrate` flag once per release,
before traffic shifts to the new revision:

```bash
az containerapp job create \
  -g <rg> -n cerdik-migrate \
  --environment <containerapps-env> \
  --trigger-type Manual --replica-timeout 600 \
  --image <acr>.azurecr.io/cerdikmy/api:<tag> \
  --command "dotnet" --args "Cerdik.Api.dll" "--migrate" \
  --secrets db-url=secretref:... \
  --env-vars DATABASE_URL=secretref:db-url

az containerapp job start -g <rg> -n cerdik-migrate
```

(Use `--args "Cerdik.Api.dll" "--seed"` for an initial demo seed in non-prod.)
The same `--migrate` / `--seed` flags described in the root `README.md` back this
job. `deploy.azcli.sh` wires this step in automatically.

## 6. Scaling

- **API / Web** — Container Apps scale on HTTP concurrency. Start `minReplicas: 1`
  (avoids cold starts on the SSE tutor endpoint), `maxReplicas: 5–10`. The SSE
  tutor stream holds a connection open per active learner, so size concurrency
  accordingly and keep the ingress idle timeout generous.
- **Worker** — Hangfire polls SQL Server; keep `minReplicas: 1`. It needs no
  ingress. Scale up only for heavy batch jobs (embedding indexing, export bundle
  generation, anonymization).
- **Azure SQL** — start at a General Purpose vCore tier; watch DTU/vCore and the
  `VECTOR` query cost. The native vector index keeps RAG retrieval cheap; the
  in-app cosine fallback is heavier, so prefer a tier/instance that supports the
  native `VECTOR` type.
- Pin a single writer for migrations (the one-off job) to avoid concurrent
  schema changes from multiple replicas.

## 7. Cost notes

- **Container Apps** bill on vCPU-seconds + memory while replicas run; scaling
  `cerdik-web`/`cerdik-api` to `minReplicas: 0` saves money but reintroduces cold
  starts (bad for the tutor's first token latency) — keep ≥1 in prod.
- **Azure SQL** is usually the largest line item; a serverless tier with
  auto-pause suits low-traffic/non-prod environments.
- **Blob Storage** is cheap (media + privacy export bundles); set lifecycle rules
  to expire old export bundles in line with the retention policy in
  [privacy-and-safety.md](privacy-and-safety.md).
- **AI provider** usage (OpenAI / Azure OpenAI / Anthropic) is billed by the
  vendor, not Azure infra — use `AI_PROVIDER=mock` in test/staging to avoid spend.
- Deploy in `southeastasia` to minimise latency to Malaysian users and avoid
  cross-region egress.
