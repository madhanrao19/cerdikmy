# cerdikMY — Azure deployment (`infra/azure`)

Deploys the cerdikMY stack to **Azure Container Apps** using Bicep.

## What gets provisioned

| Resource | Purpose |
| --- | --- |
| Azure Container Registry | Hosts the `cerdik-api` / `cerdik-web` / `cerdik-worker` images |
| Log Analytics workspace | Container Apps logs |
| Container Apps environment | Shared managed environment |
| Container App `cerdik-api` | API — external ingress on **8080** |
| Container App `cerdik-web` | Blazor web — external ingress on **8080** |
| Container App `cerdik-worker` | Hangfire worker — **no ingress** |
| Azure SQL Server + Database `cerdikmy` | Primary datastore |
| Storage Account + blob container `cerdik-media` | Media storage |

## Files

- `main.bicep` — the infrastructure template.
- `main.parameters.json` — example/placeholder parameter values. **Edit before deploying** (SQL admin, JWT secrets, AI key).
- `app-settings.example.json` — example API configuration showing the `Section__Key` env mapping.
- `deploy.azcli.sh` — end-to-end build + deploy script.

## Configuration mapping

App config keys are `Section:Key` style. In Azure they are injected as
environment variables using ASP.NET Core's double-underscore convention, so
`ConnectionStrings:Default` becomes the env var `ConnectionStrings__Default`,
`Jwt:AccessSecret` becomes `Jwt__AccessSecret`, and so on. Sensitive values are
stored as Container App secrets and referenced via `secretRef`. See
`app-settings.example.json` for the full list.

## Prerequisites

- Azure CLI (`az`) logged in: `az login`
- Subscription selected: `az account set --subscription <id>`
- Bicep (bundled with recent `az`)

## Deploy

1. Edit `main.parameters.json` and set real values for `sqlAdminLogin`,
   `sqlAdminPassword`, `jwtAccessSecret`, `jwtRefreshSecret`, and
   `aiOpenAiApiKey`. (Image refs are overridden by the script.)

2. Run the deploy script (override `RG` / `LOCATION` / `PREFIX` as needed):

   ```bash
   RG=cerdikmy-rg LOCATION=southeastasia PREFIX=cerdik \
     bash infra/azure/deploy.azcli.sh
   ```

   The script creates the resource group, builds the three images in ACR
   (`az acr build` with the repo root as context and the
   `infra/docker/*.Dockerfile` files), runs the Bicep deployment with the
   resulting image references, and prints the API/Web FQDNs.

3. Alternatively, deploy Bicep directly after building images:

   ```bash
   az deployment group create -g cerdikmy-rg -f infra/azure/main.bicep \
     -p @infra/azure/main.parameters.json \
     -p containerImageApi=<acr>.azurecr.io/cerdik-api:latest \
        containerImageWeb=<acr>.azurecr.io/cerdik-web:latest \
        containerImageWorker=<acr>.azurecr.io/cerdik-worker:latest
   ```

## Database migrations (one-off)

After the first deploy (and after schema changes), run EF Core migrations once.
Exec into a running API replica:

```bash
az containerapp exec -n cerdik-api -g cerdikmy-rg \
  --command "dotnet Cerdik.Api.dll --migrate"
```

Or run as a dedicated Container Apps job — see the notes at the bottom of
`deploy.azcli.sh`.

## Outputs

- `apiFqdn` — public hostname of the API app
- `webFqdn` — public hostname of the web app
- `acrLoginServer` — registry login server

## Notes

- The SQL firewall rule `AllowAllAzureIps` (0.0.0.0/0.0.0.0) permits
  Azure-internal traffic. Tighten with VNet integration / private endpoints for
  production hardening.
- ACR admin user is enabled so Container Apps can pull with the registry
  credentials. Consider switching to managed identity for production.
