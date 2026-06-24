# Deploying cerdikMY behind a Cloudflare Tunnel

Use this when the server has **no fixed public IP** (home/office line, NAT, dynamic IP).
A [Cloudflare Tunnel](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/)
(`cloudflared`) makes an **outbound** connection to Cloudflare, which terminates TLS at its edge
and forwards requests to a **plain-HTTP origin** in your stack. No public IP, no inbound firewall
ports, no Let's Encrypt/certbot.

```
Browser ──HTTPS──> Cloudflare edge ──tunnel──> cloudflared ──HTTP──> nginx:80 ──> web / api
```

The full stack still runs via Docker Compose (`infra/docker/docker-compose.yml`); this adds an
**nginx** origin and an **optional cloudflared** service via an overlay
(`infra/docker/docker-compose.cloudflare.yml`). nginx is the single origin: `/` → the Blazor web
app, `/api/` → the API (kept public so payment-provider **webhooks** reach
`/api/webhooks/payments/...`), with the Server-Sent-Events (tutor) and Blazor SignalR WebSocket
handling already configured.

## 1. Make the app proxy-aware
Behind a TLS-terminating proxy the origin is HTTP, so the apps must trust the forwarded headers.
This is controlled by **`BEHIND_TLS_PROXY=true`** (the overlay sets it on `web` and `api`). With it
on, both apps:
- honour `X-Forwarded-For` / `X-Forwarded-Proto`, so the real visitor IP (rate limiting, audit logs)
  and the original **https** scheme (Secure cookies, password-reset links) are recovered, and
- the web app skips origin-side `UseHttpsRedirection()` / `UseHsts()` (Cloudflare enforces HTTPS at
  the edge — redirecting at an HTTP origin would loop).

nginx passes Cloudflare's real client IP (`CF-Connecting-IP`) as `X-Forwarded-For` and forces
`X-Forwarded-Proto: https`.

## 2. Create the tunnel
In the **Cloudflare Zero Trust dashboard** → *Networks → Tunnels* → *Create a tunnel* (Cloudflared):
1. Name it (e.g. `cerdikmy`) and **copy the tunnel token**.
2. Add a **public hostname** (e.g. `app.yourdomain.my`) with the service:
   - **`http://nginx:80`** if you run cloudflared in the stack (`--profile tunnel`), or
   - **`http://localhost:80`** if you run cloudflared yourself on the host.
3. Under the tunnel's settings, ensure **WebSockets** is enabled (default), and in the zone's
   *SSL/TLS* settings turn on **Always Use HTTPS** (and optionally edge **HSTS**).

## 3. Configure `.env`
```bash
cd /opt/cerdikmy
cp .env.example .env
./scripts/generate-secrets.sh            # strong secrets; sets ALLOW_DEV_DEFAULT_SECRETS=false, SEED_DEMO_DATA=false

# then edit .env:
#   BEHIND_TLS_PROXY="true"
#   TUNNEL_TOKEN="<token from step 2>"        # only needed for --profile tunnel
#   NEXT_PUBLIC_APP_URL="https://app.yourdomain.my"
#   SESSION_COOKIE_DOMAIN="app.yourdomain.my"
#   BOOTSTRAP_ADMIN_EMAIL / BOOTSTRAP_ADMIN_PASSWORD   # your first admin
```

## 4. Bring up the stack
```bash
docker compose \
  -f infra/docker/docker-compose.yml \
  -f infra/docker/docker-compose.cloudflare.yml \
  --profile tunnel up -d --build

# Apply the schema (see deployment-hostinger.md for the migrations vs EnsureCreated note):
docker compose -f infra/docker/docker-compose.yml -f infra/docker/docker-compose.cloudflare.yml \
  run --rm api dotnet Cerdik.Api.dll --migrate
```

- **With `--profile tunnel`** the `cloudflared` container runs and dials `nginx:80` — nothing needs
  host port 80.
- **Without it**, `nginx` is still published on `127.0.0.1:80`, so a host-installed `cloudflared`
  (`cloudflared service install <token>`) can point at `http://localhost:80`.

## 5. Verify
```bash
# Local origin sanity (no Cloudflare needed) — should NOT 307-redirect-loop:
curl -i -H "X-Forwarded-Proto: https" http://127.0.0.1:80/
curl -i http://127.0.0.1:80/api/health/ready          # -> 200
curl -i http://127.0.0.1:80/api/webhooks/payments/stripe   # reaches the API, not the web app
```
Then browse `https://app.yourdomain.my`: the app loads over HTTPS, the Blazor circuit (WebSocket)
connects, the AI tutor streams, and login sets a **Secure** cookie.

## Lesson media (MinIO) caveat
Lesson media is served to the browser via **presigned object-storage URLs**. With the bundled MinIO
(`S3_ENDPOINT=http://minio:9000`, internal only) the public browser **cannot reach** those URLs.
For a tunnel deployment, either:
- add a **second public hostname** in the tunnel (e.g. `media.yourdomain.my` → `http://minio:9000`),
  then set `S3_ENDPOINT=https://media.yourdomain.my` and add it to `MEDIA_CSP_ORIGINS`; or
- use a managed bucket with public presigned URLs (Azure Blob / AWS S3) via `STORAGE_PROVIDER`.

## Notes
- This overlay is **non-breaking**: the base `docker-compose.yml` and the Hostinger nginx/certbot
  path are unchanged. Only this overlay enables tunnel mode.
- `BEHIND_TLS_PROXY` also benefits the Hostinger nginx+certbot deployment (correct client IP +
  Secure cookies) — set it there too if you front the stack with a TLS-terminating proxy.
