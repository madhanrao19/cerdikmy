#!/usr/bin/env bash
# =============================================================================
# cerdikMY — Azure deploy script
#
# Builds the api/web/worker images in Azure Container Registry (ACR) directly
# from the repo's Dockerfiles, then deploys the Bicep template that provisions
# the Container Apps stack and wires the images in.
#
# Prereqs: az CLI logged in (`az login`), an active subscription set
# (`az account set --subscription <id>`), and Bicep CLI available
# (bundled with recent az CLI).
#
# Run from anywhere; paths resolve relative to the repo root.
# =============================================================================
set -euo pipefail

# ---- Config (override via environment before running) -----------------------
RG="${RG:-cerdikmy-rg}"
LOCATION="${LOCATION:-southeastasia}"
PREFIX="${PREFIX:-cerdik}"
IMAGE_TAG="${IMAGE_TAG:-latest}"

# Resolve repo root (two levels up from infra/azure) and key paths.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
BICEP_FILE="${SCRIPT_DIR}/main.bicep"
PARAMS_FILE="${SCRIPT_DIR}/main.parameters.json"

echo "==> Resource group: ${RG} (${LOCATION})"
echo "==> Repo root:      ${REPO_ROOT}"

# ---- 1. Resource group ------------------------------------------------------
az group create --name "${RG}" --location "${LOCATION}" --output none
echo "==> Resource group ready."

# ---- 2. Ensure ACR exists (Bicep also declares it; we need it now to build) --
# The registry name must match the one main.bicep derives. We pre-create it so
# images exist before the deployment references them. Deriving the same unique
# name here requires the RG id, so instead we create a registry and reuse it.
ACR_NAME="${ACR_NAME:-${PREFIX}acr$(az group show -n "${RG}" --query id -o tsv | md5sum | cut -c1-8)}"
# ACR names must be alphanumeric and 5-50 chars.
ACR_NAME="$(echo "${ACR_NAME}" | tr -cd 'a-z0-9')"

if ! az acr show --name "${ACR_NAME}" --resource-group "${RG}" >/dev/null 2>&1; then
  echo "==> Creating ACR: ${ACR_NAME}"
  az acr create --name "${ACR_NAME}" --resource-group "${RG}" \
    --sku Basic --admin-enabled true --location "${LOCATION}" --output none
fi
ACR_LOGIN_SERVER="$(az acr show --name "${ACR_NAME}" --resource-group "${RG}" --query loginServer -o tsv)"
echo "==> ACR login server: ${ACR_LOGIN_SERVER}"

# ---- 3. Build + push the three images in ACR (context = repo root) ----------
# Each `az acr build` uploads the repo root as build context and uses the
# corresponding Dockerfile under infra/docker.
echo "==> Building cerdik-api ..."
az acr build --registry "${ACR_NAME}" \
  --image "cerdik-api:${IMAGE_TAG}" \
  --file "infra/docker/api.Dockerfile" \
  "${REPO_ROOT}"

echo "==> Building cerdik-web ..."
az acr build --registry "${ACR_NAME}" \
  --image "cerdik-web:${IMAGE_TAG}" \
  --file "infra/docker/web.Dockerfile" \
  "${REPO_ROOT}"

echo "==> Building cerdik-worker ..."
az acr build --registry "${ACR_NAME}" \
  --image "cerdik-worker:${IMAGE_TAG}" \
  --file "infra/docker/worker.Dockerfile" \
  "${REPO_ROOT}"

IMAGE_API="${ACR_LOGIN_SERVER}/cerdik-api:${IMAGE_TAG}"
IMAGE_WEB="${ACR_LOGIN_SERVER}/cerdik-web:${IMAGE_TAG}"
IMAGE_WORKER="${ACR_LOGIN_SERVER}/cerdik-worker:${IMAGE_TAG}"

# ---- 4. Deploy the Bicep template -------------------------------------------
# Image refs are passed on the command line, overriding the placeholders in
# main.parameters.json. Secrets (sql/jwt/ai) come from the parameters file —
# edit it first, or override individual -p values here.
echo "==> Deploying infrastructure ..."
az deployment group create \
  --resource-group "${RG}" \
  --template-file "${BICEP_FILE}" \
  --parameters "@${PARAMS_FILE}" \
  --parameters \
      namePrefix="${PREFIX}" \
      containerImageApi="${IMAGE_API}" \
      containerImageWeb="${IMAGE_WEB}" \
      containerImageWorker="${IMAGE_WORKER}" \
  --output json \
  --query 'properties.outputs'

# ---- 5. Print resolved outputs ----------------------------------------------
echo "==> Deployment outputs:"
API_FQDN="$(az deployment group show -g "${RG}" -n main --query 'properties.outputs.apiFqdn.value' -o tsv 2>/dev/null || true)"
WEB_FQDN="$(az deployment group show -g "${RG}" -n main --query 'properties.outputs.webFqdn.value' -o tsv 2>/dev/null || true)"
echo "    API: https://${API_FQDN}"
echo "    Web: https://${WEB_FQDN}"

# =============================================================================
# Database migrations (one-off)
# -----------------------------------------------------------------------------
# Run EF Core migrations once after the first deploy (and after schema changes).
# Two options:
#
# (A) Exec into a running API replica and invoke the migrate switch:
#
#       az containerapp exec \
#         --name "${PREFIX}-api" \
#         --resource-group "${RG}" \
#         --command "dotnet Cerdik.Api.dll --migrate"
#
# (B) Run as a dedicated one-off job (does not need a live replica):
#
#       az containerapp job create \
#         --name "${PREFIX}-migrate" \
#         --resource-group "${RG}" \
#         --environment "${PREFIX}-cae" \
#         --trigger-type Manual \
#         --replica-timeout 600 \
#         --image "${IMAGE_API}" \
#         --registry-server "${ACR_LOGIN_SERVER}" \
#         --command "dotnet" --args "Cerdik.Api.dll" "--migrate" \
#         --secrets "conn=<ConnectionStrings__Default>" \
#         --env-vars "ConnectionStrings__Default=secretref:conn"
#       az containerapp job start --name "${PREFIX}-migrate" --resource-group "${RG}"
# =============================================================================
echo "==> Done. Remember to run database migrations (see notes at end of this script)."
