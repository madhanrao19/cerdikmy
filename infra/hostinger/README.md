# cerdikMY — Hostinger VPS deployment

Self-hosted, on-prem deployment of the full cerdikMY stack (Blazor web, API,
Hangfire worker, SQL Server 2025, MinIO, mailpit) behind nginx + Let's Encrypt.

Host port map: web `5080`, API `5081`, SQL Server `1433`, MinIO `9000/9001`,
mailpit `8025`. nginx is the only thing the public internet should reach.

---

## 1. Provision the VPS

1. Create an **Ubuntu 22.04/24.04** VPS in the Hostinger panel (2 vCPU / 4 GB RAM
   minimum; SQL Server 2025 wants ~2 GB on its own).
2. SSH in as root and create the app directory:
   ```bash
   ssh root@YOUR_VPS_IP
   mkdir -p /opt/cerdikmy && cd /opt/cerdikmy
   git clone https://github.com/your-org/cerdikmy.git .
   ```
3. Open the firewall for HTTP/HTTPS only (keep DB/MinIO/mailpit private):
   ```bash
   ufw allow OpenSSH
   ufw allow 80,443/tcp
   ufw enable
   ```

## 2. Point DNS

In your DNS provider, create A/AAAA records for the app domain pointing at the
VPS IP (replace the placeholder `cerdik.example.my` used throughout):

| Record | Name              | Value         |
| ------ | ----------------- | ------------- |
| A      | cerdik.example.my | `YOUR_VPS_IP` |
| A      | www               | `YOUR_VPS_IP` |

Wait for propagation (`dig +short cerdik.example.my`) before issuing certs.

## 3. Configure environment + deploy

```bash
cd /opt/cerdikmy
cp .env.example .env
nano .env          # set real JWT_*, MSSQL/SA password, AI + payment keys
chmod +x infra/hostinger/deploy.sh
./infra/hostinger/deploy.sh
```

`deploy.sh` is idempotent: it pulls latest code, installs Docker + the Compose
plugin if missing, builds/starts the stack, and runs EF migrations via
`docker compose run --rm api dotnet Cerdik.Api.dll --migrate`.

> **Run as a service instead?** Use the systemd unit:
> ```bash
> cp infra/hostinger/cerdikmy.service /etc/systemd/system/
> systemctl daemon-reload && systemctl enable --now cerdikmy.service
> ```

## 4. nginx + certbot (TLS)

```bash
apt-get install -y nginx certbot python3-certbot-nginx
mkdir -p /var/www/certbot

cp infra/hostinger/nginx.conf /etc/nginx/sites-available/cerdikmy.conf
ln -sf /etc/nginx/sites-available/cerdikmy.conf /etc/nginx/sites-enabled/
rm -f /etc/nginx/sites-enabled/default
nginx -t && systemctl reload nginx

# Issue + auto-install the certificate (edits the served vhost in place):
certbot --nginx -d cerdik.example.my -d www.cerdik.example.my \
        --redirect --agree-tos -m admin@cerdik.example.my --no-eff-email
```

After issuance, enable the TLS `server { listen 443 ... }` block in
`nginx.conf` and switch the `:80` block to `return 301 https://$host$request_uri;`.
Renewal is automatic via the certbot systemd timer (`systemctl list-timers | grep certbot`).

The tutor endpoint `/api/tutor` is configured for **SSE** (`proxy_buffering off`,
1-hour read timeout); the root `/` carries the **WebSocket upgrade** headers
Blazor Server's SignalR circuit needs.

## 5. Backups

### SQL Server (logical backup → host-mounted dir, nightly cron)

Back up the `cerdikmy` database inside the running `cerdik-mssql` container.
Add to root's crontab (`crontab -e`):

```cron
# 02:15 daily — full backup of cerdikmy to /var/opt/mssql/backup inside the container
15 2 * * * docker exec cerdik-mssql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -No -Q "BACKUP DATABASE [cerdikmy] TO DISK = N'/var/opt/mssql/backup/cerdikmy_$(date +\%F).bak' WITH INIT, COMPRESSION, STATS = 10"
```

> The `.bak` lands inside the `cerdik-mssql-data` volume (`/var/opt/mssql`), so
> it is captured by the volume backup below. `$MSSQL_SA_PASSWORD` must be set in
> root's cron environment (or inline the value).

### Docker volumes (off-box copy)

The stateful data lives in two named volumes — `cerdik-mssql-data` and
`cerdik-minio-data`. Snapshot them to tarballs (then ship off-box, e.g. to S3):

```bash
# Quiesce SQL Server first for a consistent volume snapshot.
docker compose -f infra/docker/docker-compose.yml stop mssql

docker run --rm \
  -v cerdik-mssql-data:/data:ro \
  -v /opt/cerdikmy/backups:/backup \
  alpine tar czf /backup/mssql-data-$(date +%F).tar.gz -C /data .

docker run --rm \
  -v cerdik-minio-data:/data:ro \
  -v /opt/cerdikmy/backups:/backup \
  alpine tar czf /backup/minio-data-$(date +%F).tar.gz -C /data .

docker compose -f infra/docker/docker-compose.yml start mssql
```

Restore is the reverse: `tar xzf ... -C /data` into a fresh volume before
`up -d`. Keep at least one copy off the VPS.

## 6. Updating

Re-run the deploy script to roll forward — it rebuilds changed images, recreates
containers, and re-applies migrations:

```bash
cd /opt/cerdikmy && ./infra/hostinger/deploy.sh
```
