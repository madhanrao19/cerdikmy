# Deployment — Hostinger VPS (self-hosted)

This guide deploys the **full cerdikMY stack** (Blazor web, API, Hangfire worker,
SQL Server 2025, MinIO, mailpit) to a single Hostinger Ubuntu VPS using Docker
Compose behind an nginx reverse proxy with Let's Encrypt TLS.

> **No fixed public IP?** Use a Cloudflare Tunnel instead (TLS at the edge, no inbound
> ports, no certbot) — see [deployment-cloudflare.md](deployment-cloudflare.md).

The repo ships the assets you need under `infra/hostinger/`:

- `deploy.sh` — idempotent deploy script (pull, install Docker, build, migrate).
- `nginx.conf` — reverse proxy with SSE + Blazor WebSocket handling.
- `README.md` — the quick checklist this document expands on.

> nginx is the **only** thing the public internet should reach. The containers
> publish to `127.0.0.1` only (web `5080`, API `5081`, SQL Server `1433`, MinIO
> `9000/9001`, mailpit `8025`).

---

## 1. Provision the VPS

1. Create an **Ubuntu 22.04 or 24.04** VPS in the Hostinger panel. Minimum
   **2 vCPU / 4 GB RAM** — SQL Server 2025 alone wants ~2 GB. 8 GB is comfortable
   once the AI worker and web app are loaded.
2. SSH in as root and clone into `/opt/cerdikmy`:
   ```bash
   ssh root@YOUR_VPS_IP
   mkdir -p /opt/cerdikmy && cd /opt/cerdikmy
   git clone https://github.com/your-org/cerdikmy.git .
   ```
3. Lock the firewall to SSH + HTTP/HTTPS only (keep DB/MinIO/mailpit private):
   ```bash
   ufw allow OpenSSH
   ufw allow 80,443/tcp
   ufw enable
   ```

## 2. Point DNS

Create A/AAAA records for the app domain at the VPS IP (the assets use the
placeholder `cerdik.example.my` — replace it throughout):

| Record | Name              | Value         |
| ------ | ----------------- | ------------- |
| A      | cerdik.example.my | `YOUR_VPS_IP` |
| A      | www               | `YOUR_VPS_IP` |

Wait for propagation before issuing certificates:

```bash
dig +short cerdik.example.my
```

## 3. Configure environment + deploy

```bash
cd /opt/cerdikmy
cp .env.example .env
nano .env          # set real JWT_*, MSSQL/SA password, AI keys, payment keys
chmod +x infra/hostinger/deploy.sh
./infra/hostinger/deploy.sh
```

`deploy.sh` is **idempotent** — safe to re-run. It:

1. Fetches and hard-resets to `origin/${GIT_BRANCH:-main}`.
2. Installs Docker Engine + the Compose plugin if missing.
3. Bootstraps `.env` from `.env.example` if absent (then warns you to set real
   secrets before exposing the stack).
4. Pulls upstream images, then `docker compose ... up -d --build`.
5. Applies EF Core migrations via a one-off run:
   `docker compose run --rm api dotnet Cerdik.Api.dll --migrate`.
6. Prints container status and the local health URLs (bound to `127.0.0.1`).

> **Run as a managed service?** Install the systemd unit so the stack starts on
> boot:
> ```bash
> cp infra/hostinger/cerdikmy.service /etc/systemd/system/
> systemctl daemon-reload && systemctl enable --now cerdikmy.service
> ```

## 4. nginx reverse proxy + TLS (certbot)

```bash
apt-get install -y nginx certbot python3-certbot-nginx
mkdir -p /var/www/certbot

cp infra/hostinger/nginx.conf /etc/nginx/sites-available/cerdikmy.conf
ln -sf /etc/nginx/sites-available/cerdikmy.conf /etc/nginx/sites-enabled/
rm -f /etc/nginx/sites-enabled/default
nginx -t && systemctl reload nginx

# Issue + auto-install the cert (also writes the ACME http-01 challenge route):
certbot --nginx -d cerdik.example.my -d www.cerdik.example.my \
        --redirect --agree-tos -m admin@cerdik.example.my --no-eff-email
```

After issuance, enable the `server { listen 443 ssl http2; ... }` block in
`nginx.conf` and switch the `:80` block to a pure redirect
(`return 301 https://$host$request_uri;`). Renewal is automatic via the certbot
systemd timer (`systemctl list-timers | grep certbot`).

### Routing notes — SSE and Blazor WebSocket

`nginx.conf` already encodes the two tricky cases:

- **`/api/tutor` (Server-Sent Events).** This `location ^~ /api/tutor` block comes
  *before* the generic `/api/` block so it wins longest-prefix match. It sets
  `proxy_buffering off`, `proxy_cache off`, `chunked_transfer_encoding off`,
  empties the `Connection` header, and uses a **1-hour** read/send timeout so
  tutor tokens flush to the browser immediately and long generations don't get
  cut off.
- **`/` (Blazor Server / SignalR circuit).** The root block forwards the
  `Upgrade` / `Connection` headers (via the `map $http_upgrade $connection_upgrade`
  block) so the WebSocket upgrade the SignalR circuit needs passes through. Keep
  the same headers in the TLS block when you enable it.

## 5. SQL Server backups (nightly cron)

### Logical DB backup inside the container

Back up the `cerdikmy` database in the running `cerdik-mssql` container. Add to
root's crontab (`crontab -e`):

```cron
# 02:15 daily — full backup of cerdikmy to /var/opt/mssql/backup (inside the volume)
15 2 * * * docker exec cerdik-mssql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -No -Q "BACKUP DATABASE [cerdikmy] TO DISK = N'/var/opt/mssql/backup/cerdikmy_$(date +\%F).bak' WITH INIT, COMPRESSION, STATS = 10"
```

`$MSSQL_SA_PASSWORD` must be set in root's cron environment (or inline the value).
The `.bak` lands in the `cerdik-mssql-data` volume, so it is captured by the
volume snapshot below.

### Off-box volume snapshots

The stateful data lives in two named volumes — `cerdik-mssql-data` and
`cerdik-minio-data`. Quiesce SQL Server, tar them, then ship the tarballs off the
VPS (e.g. to S3/Azure Blob):

```bash
docker compose -f infra/docker/docker-compose.yml stop mssql

docker run --rm -v cerdik-mssql-data:/data:ro -v /opt/cerdikmy/backups:/backup \
  alpine tar czf /backup/mssql-data-$(date +%F).tar.gz -C /data .
docker run --rm -v cerdik-minio-data:/data:ro -v /opt/cerdikmy/backups:/backup \
  alpine tar czf /backup/minio-data-$(date +%F).tar.gz -C /data .

docker compose -f infra/docker/docker-compose.yml start mssql
```

Keep at least one copy off the VPS. Restore is the reverse: `tar xzf ... -C /data`
into a fresh volume before `up -d`.

## 6. Secret hardening

Before exposing the stack publicly:

- **Rotate every default secret in `.env`** — `JWT_ACCESS_SECRET`,
  `JWT_REFRESH_SECRET` (≥32 chars each), the SQL `sa` / `MSSQL_SA_PASSWORD`, and
  `S3_ACCESS_KEY` / `S3_SECRET_KEY` (do not ship `minioadmin/minioadmin`).
- Set real `AI_PROVIDER` keys and payment keys (Billplz / Curlec / Stripe).
- Keep `.env` at `chmod 600` and owned by root; it is git-ignored — never commit
  it.
- Do **not** publish ports 1433 / 9000 / 9001 / 8025 to the internet; the firewall
  (step 1) already blocks them, and the containers bind to `127.0.0.1`.
- In production set `STORAGE_PROVIDER=s3` against a hardened MinIO (non-default
  creds, TLS) or switch to `azure` Blob — see
  [deployment-azure.md](deployment-azure.md).
- Mailpit is a dev-only mail sink. For real outbound mail, point `SMTP_URL` /
  `MAIL_FROM` at a real relay and stop exposing mailpit.

## 7. Upgrade and rollback

**Roll forward** — just re-run the idempotent deploy script. It rebuilds changed
images, recreates containers, and re-applies migrations:

```bash
cd /opt/cerdikmy && ./infra/hostinger/deploy.sh
```

**Rollback** — check out a known-good tag/commit and redeploy:

```bash
cd /opt/cerdikmy
git fetch --tags
GIT_BRANCH=v1.4.2 ./infra/hostinger/deploy.sh   # or: git checkout <good-sha> && deploy.sh
```

> EF Core migrations are forward-only. Before a risky upgrade, take a fresh DB
> backup (step 5). If a migration must be undone, restore the pre-upgrade `.bak`
> rather than relying on a down-migration. Pin image tags for reproducible
> rollbacks rather than depending solely on `latest`.
