#!/usr/bin/env bash
# =============================================================================
# cerdikMY — Hostinger VPS deploy script (Ubuntu)
#
# Idempotent: safe to re-run. It pulls latest code, ensures Docker is present,
# bootstraps .env, (re)builds + starts the stack, applies EF migrations, and
# prints the health URLs.
#
# Usage:
#   ssh root@your-vps
#   cd /opt/cerdikmy && ./infra/hostinger/deploy.sh
# =============================================================================
set -euo pipefail

# --- Resolve repo root regardless of where the script is invoked from --------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
cd "${REPO_ROOT}"

COMPOSE_FILE="infra/docker/docker-compose.yml"
ENV_FILE=".env"
GIT_BRANCH="${GIT_BRANCH:-main}"

log()  { printf '\033[1;34m[deploy]\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[warn]\033[0m %s\n'   "$*"; }
err()  { printf '\033[1;31m[error]\033[0m %s\n'  "$*" >&2; }

# -----------------------------------------------------------------------------
# 1. Pull latest source.
# -----------------------------------------------------------------------------
if [ -d .git ]; then
  log "Fetching latest from origin/${GIT_BRANCH} ..."
  git fetch --prune origin
  git checkout "${GIT_BRANCH}"
  git reset --hard "origin/${GIT_BRANCH}"
else
  warn "Not a git checkout; skipping git pull."
fi

# -----------------------------------------------------------------------------
# 2. Ensure Docker Engine + Compose plugin are installed.
# -----------------------------------------------------------------------------
if ! command -v docker >/dev/null 2>&1; then
  log "Docker not found — installing via the official convenience script ..."
  curl -fsSL https://get.docker.com | sh
  systemctl enable --now docker
else
  log "Docker present: $(docker --version)"
fi

if ! docker compose version >/dev/null 2>&1; then
  log "Docker Compose plugin missing — installing docker-compose-plugin ..."
  apt-get update -y
  apt-get install -y docker-compose-plugin
else
  log "Compose present: $(docker compose version | head -n1)"
fi

# -----------------------------------------------------------------------------
# 3. Bootstrap .env from the example if absent.
# -----------------------------------------------------------------------------
if [ ! -f "${ENV_FILE}" ]; then
  if [ -f .env.example ]; then
    cp .env.example "${ENV_FILE}"
    warn ".env was missing — copied from .env.example."
    warn "EDIT ${REPO_ROOT}/${ENV_FILE} NOW: set real secrets (JWT_*, SA password,"
    warn "AI keys, payment keys) before this stack is exposed to the internet."
  else
    err ".env and .env.example both missing — cannot continue."
    exit 1
  fi
else
  log ".env present — leaving it untouched."
fi

# -----------------------------------------------------------------------------
# 4. Pull base images, then build + (re)create the stack.
# -----------------------------------------------------------------------------
log "Pulling upstream images (mssql, minio, mailpit, ...) ..."
# `pull` only touches services with an `image:` and no local build; ignore the
# expected non-zero for build-only services to stay idempotent.
docker compose -f "${COMPOSE_FILE}" --env-file "${ENV_FILE}" pull --ignore-buildable || true

log "Building and starting services ..."
docker compose -f "${COMPOSE_FILE}" --env-file "${ENV_FILE}" up -d --build

# -----------------------------------------------------------------------------
# 5. Apply EF Core migrations via a one-off API run.
#    The API binary supports a --migrate flag that runs DbContext.Migrate()
#    and exits. --rm disposes the throwaway container afterwards.
# -----------------------------------------------------------------------------
log "Applying database migrations ..."
docker compose -f "${COMPOSE_FILE}" --env-file "${ENV_FILE}" \
  run --rm api dotnet Cerdik.Api.dll --migrate

# -----------------------------------------------------------------------------
# 6. Report.
# -----------------------------------------------------------------------------
log "Deployment complete. Service health:"
docker compose -f "${COMPOSE_FILE}" --env-file "${ENV_FILE}" ps

cat <<'EOF'

  Local health URLs (bind to 127.0.0.1; expose via nginx):
    Web (Blazor)   : http://127.0.0.1:5080
    API            : http://127.0.0.1:5081
    MinIO console  : http://127.0.0.1:9001   (dev only)
    Mailpit UI     : http://127.0.0.1:8025   (dev only)

  Next: configure nginx (infra/hostinger/nginx.conf) + certbot for TLS.
EOF
