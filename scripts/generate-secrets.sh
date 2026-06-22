#!/usr/bin/env bash
# =============================================================================
# Generate strong production secrets for cerdikMY and write them to .env.
#
# Produces cryptographically-random values for the SQL Server sa password, the
# MinIO root credentials and the JWT signing keys, then writes (or updates) the
# repository-root .env that docker-compose reads. Existing non-secret settings
# in .env are preserved; only the secret keys below are (re)written.
#
# Usage:
#   ./scripts/generate-secrets.sh                # create/refresh ./.env
#   ENV_FILE=/opt/cerdikmy/.env ./scripts/generate-secrets.sh
#
# Safe to re-run: it rotates the secrets. Restart the stack afterwards.
# =============================================================================
set -euo pipefail
cd "$(dirname "$0")/.."

ENV_FILE="${ENV_FILE:-.env}"

command -v openssl >/dev/null 2>&1 || { echo "openssl is required" >&2; exit 1; }

# URL/connection-string-safe random strings (no '/', '+', '=', or quotes).
rand() { openssl rand -base64 48 | tr -dc 'A-Za-z0-9' | head -c "${1:-40}"; }

SA_PASSWORD="$(rand 28)Aa1!"            # satisfy SQL Server complexity rules
S3_ACCESS_KEY="$(rand 20)"
S3_SECRET_KEY="$(rand 40)"
JWT_ACCESS_SECRET="$(rand 48)"
JWT_REFRESH_SECRET="$(rand 48)"

DATABASE_URL="Server=mssql,1433;Database=cerdikmy;User Id=sa;Password=${SA_PASSWORD};TrustServerCertificate=True;Encrypt=False"

# Upsert a KEY="VALUE" line into the env file, replacing any existing definition.
upsert() {
  local key="$1" value="$2"
  touch "$ENV_FILE"
  if grep -qE "^${key}=" "$ENV_FILE"; then
    # Use a non-/ delimiter; values are alphanumeric so this is safe.
    grep -vE "^${key}=" "$ENV_FILE" > "$ENV_FILE.tmp" && mv "$ENV_FILE.tmp" "$ENV_FILE"
  fi
  printf '%s="%s"\n' "$key" "$value" >> "$ENV_FILE"
}

echo "==> Writing production secrets to ${ENV_FILE}"
upsert MSSQL_SA_PASSWORD "$SA_PASSWORD"
upsert DATABASE_URL "$DATABASE_URL"
upsert S3_ACCESS_KEY "$S3_ACCESS_KEY"
upsert S3_SECRET_KEY "$S3_SECRET_KEY"
upsert JWT_ACCESS_SECRET "$JWT_ACCESS_SECRET"
upsert JWT_REFRESH_SECRET "$JWT_REFRESH_SECRET"
# Production must NOT tolerate dev-default credentials, and must not seed demo data.
upsert ALLOW_DEV_DEFAULT_SECRETS "false"
upsert SEED_DEMO_DATA "false"

chmod 600 "$ENV_FILE" 2>/dev/null || true

echo "==> Done. Generated strong secrets for: sa password, MinIO keys, JWT keys."
echo "    Review ${ENV_FILE}, set the remaining provider keys (AI / payments / SMTP),"
echo "    then (re)start the stack:"
echo "      docker compose -f infra/docker/docker-compose.yml --env-file ${ENV_FILE} up -d --build"
echo
echo "    NOTE: rotating JWT secrets invalidates existing sessions; rotating the sa"
echo "    password requires the SQL Server volume to be re-initialised or the password"
echo "    changed inside the running container."
