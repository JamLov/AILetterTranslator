#!/usr/bin/env bash
set -euo pipefail

# ---------------------------------------------------------------------------
# Safety: only allow releases from main
# ---------------------------------------------------------------------------
CURRENT_BRANCH=$(git -C "$(dirname "${BASH_SOURCE[0]}")/.." rev-parse --abbrev-ref HEAD)
if [[ "$CURRENT_BRANCH" != "main" ]]; then
    echo "ERROR: Releases must be run from 'main'. Currently on '$CURRENT_BRANCH'."
    exit 1
fi

# ============================================================================
# release.sh — Build new container images and roll them out via Terraform.
#
# Purpose
#   - Discover the next v0.0.N tag in ACR.
#   - Build and push lt-backend, lt-frontend, lt-worker with both the new
#     versioned tag and :latest.
#   - Call `terraform apply -var image_tag=v0.0.N` in ../infra-terraform/ to
#     roll out new revisions of the three container resources.
#
# What this script does NOT do
#   - It does not provision Azure infrastructure. That is owned by Terraform
#     in ../infra-terraform/ (see ADR 016).
#   - It does not bootstrap the Terraform state backend. See
#     ../infra-terraform/bootstrap/bootstrap.sh.
#   - It does not create role assignments, key vaults, container apps, etc.
#     If you are deploying to a brand-new subscription, see the README for
#     the greenfield bootstrap sequence.
#
# Prerequisites
#   - `infra-terraform/` has been initialised against the target subscription
#     (terraform init, imports absorbed, plan shows no changes).
#   - infra/.env.azure exists (provides GOOGLE_CLIENT_ID for the frontend
#     build arg — Gemini key and other secrets live in Key Vault).
#   - You are logged in: `az login` + `az account set --subscription <id>`.
#
# Usage
#   cd infra && ./release.sh
# ============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
TF_DIR="$PROJECT_ROOT/infra-terraform"

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
# Convert WSL /mnt/d/... paths to Windows D:/... (az CLI is Windows-native).
to_win_path() {
    if [[ "$1" =~ ^/mnt/([a-z])/(.*) ]]; then
        echo "${BASH_REMATCH[1]^^}:/${BASH_REMATCH[2]}"
    else
        echo "$1"
    fi
}

# Strip Windows \r from az CLI output (subtle bug when piping to bash vars).
azq() {
    az "$@" | tr -d '\r'
}

# Highest existing v0.0.N tag in a single ACR repo; 0 if none.
get_max_version_in_repo() {
    local repo=$1
    local tags
    tags=$(azq acr repository show-tags --name "$ACR_NAME" --repository "$repo" --output tsv 2>/dev/null || true)
    if [[ -z "$tags" ]]; then echo "0"; return; fi
    local max
    max=$(echo "$tags" | grep -E '^v0\.0\.[0-9]+$' | sed 's/^v0\.0\.//' | sort -n | tail -1 || true)
    echo "${max:-0}"
}

WIN_PROJECT_ROOT="$(to_win_path "$PROJECT_ROOT")"

# ---------------------------------------------------------------------------
# Load configuration (just GOOGLE_CLIENT_ID is needed at build time)
# ---------------------------------------------------------------------------
ENV_FILE="$SCRIPT_DIR/.env.azure"
if [[ ! -f "$ENV_FILE" ]]; then
    echo "ERROR: $ENV_FILE not found."
    exit 1
fi
set -a
source "$ENV_FILE"
set +a

if [[ -z "${GOOGLE_CLIENT_ID:-}" ]]; then
    echo "ERROR: GOOGLE_CLIENT_ID not set in $ENV_FILE (needed as a frontend build arg)."
    exit 1
fi

# ---------------------------------------------------------------------------
# Derive ACR name (same SHA-256 suffix logic Terraform uses)
# ---------------------------------------------------------------------------
SUB_ID=$(azq account show --query "id" -o tsv)
SUFFIX=$(printf '%s' "$SUB_ID" | openssl dgst -sha256 2>/dev/null | sed 's/.*= //' | cut -c1-6)
ACR_NAME="ltacr${SUFFIX}"

# ---------------------------------------------------------------------------
# Determine next version
# ---------------------------------------------------------------------------
echo "============================================"
echo "Release: discovering next version..."
echo "============================================"

MAX_VERSION=0
for repo in lt-backend lt-frontend lt-worker; do
    v=$(get_max_version_in_repo "$repo")
    echo "   ${repo}: max existing version = v0.0.${v}"
    if [[ "$v" -gt "$MAX_VERSION" ]]; then
        MAX_VERSION=$v
    fi
done
NEW_VERSION=$((MAX_VERSION + 1))
IMAGE_TAG="v0.0.${NEW_VERSION}"
echo "   Next version: $IMAGE_TAG"
echo ""

# ---------------------------------------------------------------------------
# Build images (cloud-side via ACR Tasks; tags both versioned and :latest)
# ---------------------------------------------------------------------------
echo "============================================"
echo "Building & pushing $IMAGE_TAG..."
echo "============================================"

echo ">> backend..."
az acr build \
    --registry "$ACR_NAME" \
    --image "lt-backend:${IMAGE_TAG}" \
    --image "lt-backend:latest" \
    --file "$WIN_PROJECT_ROOT/app/backend/Dockerfile" \
    "$WIN_PROJECT_ROOT/app/"

echo ">> worker..."
az acr build \
    --registry "$ACR_NAME" \
    --image "lt-worker:${IMAGE_TAG}" \
    --image "lt-worker:latest" \
    --file "$WIN_PROJECT_ROOT/app/worker/Dockerfile" \
    "$WIN_PROJECT_ROOT/app/"

echo ">> frontend..."
az acr build \
    --registry "$ACR_NAME" \
    --image "lt-frontend:${IMAGE_TAG}" \
    --image "lt-frontend:latest" \
    --file "$WIN_PROJECT_ROOT/app/frontend/Dockerfile" \
    --build-arg "VITE_GOOGLE_CLIENT_ID=$GOOGLE_CLIENT_ID" \
    "$WIN_PROJECT_ROOT/app/frontend/"

# ---------------------------------------------------------------------------
# Roll out via Terraform
# ---------------------------------------------------------------------------
echo ""
echo "============================================"
echo "Deploying $IMAGE_TAG via Terraform..."
echo "============================================"
echo "   (cd $TF_DIR && terraform apply -var image_tag=$IMAGE_TAG)"

cd "$TF_DIR"
terraform apply -auto-approve -var "image_tag=$IMAGE_TAG"

# ---------------------------------------------------------------------------
# Output
# ---------------------------------------------------------------------------
FRONTEND_URL=$(terraform output -raw frontend_url 2>/dev/null || echo "unknown")

echo ""
echo "============================================"
echo "Release $IMAGE_TAG deployed."
echo "============================================"
echo ""
echo "Frontend:  $FRONTEND_URL"
echo "Version:   $IMAGE_TAG"
echo "ACR tags:  lt-{backend,frontend,worker}:${IMAGE_TAG} and :latest"
echo ""
echo "To check what is actually running:"
echo "  cd $TF_DIR && terraform output deployed_image_tag"
echo ""
