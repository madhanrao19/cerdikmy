# Production Readiness

This document tracks operational hardening for cerdikMY: what ships in the codebase today, and the
remaining steps an operator should take before/while running in production. It complements
[deployment-hostinger.md](./deployment-hostinger.md) and [deployment-azure.md](./deployment-azure.md).

## ✅ In the codebase

| Area | What's implemented |
| --- | --- |
| **Rate limiting** | Per-IP limiter (`src/Cerdik.Api/RateLimitingSetup.cs`): global 120/min, `auth` 10/min, `tutor` 30/min (AI cost/abuse control). Webhooks exempt. Returns `429` + `Retry-After`. Disabled under the Testing environment. |
| **Security headers** | `SecurityHeadersMiddleware` sets `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Cross-Origin-Resource-Policy`, `Permissions-Policy`; strips `Server`. |
| **Health checks** | `/health/live` (process liveness), `/health/ready` (DB reachable via `DbHealthCheck`), `/health` (all). Containers declare a `HEALTHCHECK`; `web` waits for `api` to be healthy. |
| **Fail-fast config** | `StartupValidation` rejects empty/short (<32 char) JWT signing keys at boot and warns when dev defaults are used in Production. |
| **AuthN/AuthZ** | JWT access tokens (httpOnly cookie + bearer), rotating refresh tokens (hashed at rest), RBAC roles, per-endpoint authorization. |
| **Secrets in transit/at rest** | Refresh tokens HMAC-hashed; webhook signatures verified (Billplz/Curlec/Stripe); payment payloads redacted before persistence. |
| **Observability** | OpenTelemetry tracing (ASP.NET Core + HttpClient) with OTLP exporter; Serilog structured logging + request logging. |
| **Privacy/compliance** | PDPA consent capture, export & delete/anonymize jobs, append-only audit log, no copyrighted KPM content. |
| **CI** | Build + unit + integration tests + Docker image builds on every push/PR. |

## ⛳ Recommended before production

These need an environment with the .NET SDK and/or cloud access, so they are intentionally left to the
operator rather than hard-coded.

### 1. Real EF Core migrations (replace `EnsureCreated`)
First boot currently calls `EnsureCreated` + applies the native `VECTOR` index. For schema evolution,
generate a migration history:

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add Initial -p src/Cerdik.Infrastructure -s src/Cerdik.Api
# Review the generated migration, then on deploy:
dotnet Cerdik.Api.dll --migrate
```

Once migrations exist, `DbInitializer` automatically prefers `Migrate()` over `EnsureCreated()`.

### 2. Dependency vulnerability audit
CI surfaces transitive advisories (e.g. `System.Security.Cryptography.Xml`, `OpenTelemetry.Api`).
Resolve them with a tool that can reach NuGet (not pinned blindly here to avoid breaking a verified build):

```bash
dotnet restore
dotnet list package --vulnerable --include-transitive
# Add a pinned, patched <PackageVersion> to Directory.Packages.props for each flagged package,
# then re-run the audit until clean. Consider enabling <NuGetAudit>true</NuGetAudit> as a gate.
```

### 3. Secrets management
Replace `.env`/compose defaults with a real secret store:
- **Azure**: Key Vault references in Container Apps (the Bicep wires app secrets; point them at Key Vault).
- **Hostinger/VPS**: Docker secrets or a vault agent; never commit real secrets.
- Rotate `JWT_ACCESS_SECRET` / `JWT_REFRESH_SECRET` (32+ chars), DB credentials, and provider keys.

### 4. TLS & reverse proxy
Terminate TLS at nginx (see `infra/hostinger/nginx.conf`) with certbot auto-renewal. Keep SSE
(`/api/tutor`) unbuffered and the Blazor WebSocket upgrade headers in place. Add proxy-level rate
limiting (`limit_req_zone`) as a second layer in front of the app limiter.

### 5. Backups & DR
- SQL Server: scheduled `BACKUP DATABASE` (see deployment-hostinger.md) + offsite copy; test restores.
- Object storage: enable versioning/lifecycle on the prod bucket/container.
- Document RPO/RTO targets.

### 6. Monitoring & alerting
- Point the OTLP exporter at a collector (Tempo/Jaeger/App Insights) and add metrics + dashboards.
- Alert on: `/health/ready` failing, 5xx rate, auth 429 spikes, Hangfire failed jobs, AI provider errors,
  webhook signature failures.

### 7. Scaling notes
- API and Worker are stateless and scale horizontally; the Blazor Server app uses SignalR — enable
  sticky sessions or the Azure SignalR Service when running multiple web replicas.
- Hangfire storage is SQL Server; the recurring `recompute-mastery` job is owned by the Worker.
- For large RAG corpora, switch retrieval to SQL Server native `VECTOR_DISTANCE` ANN (the column and a
  commented native query path already exist) instead of the in-process cosine fallback.

### 8. Abuse & safety
- Tune the `tutor` rate limit to your AI budget; add per-student quotas if needed.
- Keep the two-stage moderation enabled; staff the `SafetyReviewer` queue and alert on escalations.
