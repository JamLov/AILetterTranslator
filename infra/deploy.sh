#!/usr/bin/env bash
set -euo pipefail

# ============================================================================
# deploy.sh — Deploy Letter Translation to Azure Container Apps
#
# Creates all Azure resources from scratch, builds Docker images via ACR Tasks,
# and deploys three Container Apps (frontend, backend, worker).
#
# Prerequisites:
#   - Azure CLI installed and logged in (az login)
#   - infra/.env.azure populated (copy from .env.azure.example)
#
# Usage:
#   cd infra && ./deploy.sh
# ============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# ---------------------------------------------------------------------------
# Helper: convert WSL /mnt/d/... paths to Windows D:/... paths
# (needed because az CLI is Windows-native but bash resolves WSL paths)
# ---------------------------------------------------------------------------
to_win_path() {
    if [[ "$1" =~ ^/mnt/([a-z])/(.*) ]]; then
        echo "${BASH_REMATCH[1]^^}:/${BASH_REMATCH[2]}"
    else
        echo "$1"
    fi
}

WIN_PROJECT_ROOT="$(to_win_path "$PROJECT_ROOT")"

# ---------------------------------------------------------------------------
# Helper: capture az CLI output, stripping Windows \r characters
# ---------------------------------------------------------------------------
azq() {
    az "$@" | tr -d '\r'
}

# ---------------------------------------------------------------------------
# Load configuration
# ---------------------------------------------------------------------------
ENV_FILE="$SCRIPT_DIR/.env.azure"
if [[ ! -f "$ENV_FILE" ]]; then
    echo "ERROR: $ENV_FILE not found. Copy .env.azure.example and fill in your values."
    exit 1
fi
set -a
source "$ENV_FILE"
set +a

# Validate required vars
for var in GOOGLE_CLIENT_ID ALLOWED_USERS GEMINI_API_KEY; do
    if [[ -z "${!var:-}" ]]; then
        echo "ERROR: $var is not set in $ENV_FILE"
        exit 1
    fi
done
GEMINI_MODEL="${GEMINI_MODEL:-gemini-2.5-pro}"

# ---------------------------------------------------------------------------
# Azure resource names
# ---------------------------------------------------------------------------
RESOURCE_GROUP="lt-rg"
LOCATION="uksouth"
BLOB_CONTAINER="letter-translation"
ENVIRONMENT_NAME="lt-env"
FRONTEND_APP="lt-frontend"
BACKEND_APP="lt-backend"
WORKER_JOB="lt-worker"

# Globally unique names: deterministic suffix from subscription ID
SUB_ID=$(azq account show --query "id" -o tsv)
SUFFIX=$(printf '%s' "$SUB_ID" | openssl dgst -sha256 2>/dev/null | sed 's/.*= //' | cut -c1-6)
ACR_NAME="ltacr${SUFFIX}"
STORAGE_ACCOUNT="ltstorage${SUFFIX}"
KEYVAULT_NAME="lt-kv-${SUFFIX}"

echo "============================================"
echo "Letter Translation — Azure Deployment"
echo "============================================"
echo "Resource Group:    $RESOURCE_GROUP"
echo "Location:          $LOCATION"
echo "ACR:               $ACR_NAME"
echo "Storage Account:   $STORAGE_ACCOUNT"
echo "Key Vault:         $KEYVAULT_NAME"
echo "Environment:       $ENVIRONMENT_NAME"
echo "Unique suffix:     $SUFFIX"
echo "============================================"
echo ""

# ---------------------------------------------------------------------------
# Helper: retry a command with backoff (for RBAC propagation)
# ---------------------------------------------------------------------------
retry() {
    local max_attempts=$1
    local delay=$2
    shift 2
    for attempt in $(seq 1 "$max_attempts"); do
        if "$@" 2>/dev/null; then
            return 0
        fi
        if [[ $attempt -lt $max_attempts ]]; then
            echo "   Attempt $attempt/$max_attempts failed, retrying in ${delay}s..."
            sleep "$delay"
        fi
    done
    echo "   Final attempt..."
    "$@"
}

# ---------------------------------------------------------------------------
# Step 0: Register required resource providers (one-time per subscription)
# ---------------------------------------------------------------------------
echo ">> Step 0: Registering resource providers (if needed)..."
for provider in Microsoft.ContainerRegistry Microsoft.Storage Microsoft.KeyVault \
                Microsoft.App Microsoft.OperationalInsights; do
    state=$(azq provider show --namespace "$provider" --query "registrationState" -o tsv 2>/dev/null || echo "NotRegistered")
    state=$(echo "$state" | tr -d '[:space:]')
    if [[ "$state" != "Registered" ]]; then
        echo "   Registering $provider..."
        az provider register --namespace "$provider" --output none
    fi
done

# Wait for providers to finish registering
echo "   Waiting for provider registration..."
for provider in Microsoft.ContainerRegistry Microsoft.Storage Microsoft.KeyVault \
                Microsoft.App Microsoft.OperationalInsights; do
    while true; do
        state=$(azq provider show --namespace "$provider" --query "registrationState" -o tsv)
        state=$(echo "$state" | tr -d '[:space:]')
        echo "   $provider: $state"
        [[ "$state" == "Registered" ]] && break
        sleep 5
    done
done
echo "   All providers registered."

# ---------------------------------------------------------------------------
# Step 1: Resource Group
# ---------------------------------------------------------------------------
echo ">> Step 1: Creating resource group..."
az group create --name "$RESOURCE_GROUP" --location "$LOCATION" --output none

# ---------------------------------------------------------------------------
# Step 2: Azure Container Registry
# ---------------------------------------------------------------------------
echo ">> Step 2: Container registry ($ACR_NAME)..."
if az acr show --name "$ACR_NAME" --output none 2>/dev/null; then
    echo "   Already exists, skipping."
else
    az acr create \
        --resource-group "$RESOURCE_GROUP" \
        --name "$ACR_NAME" \
        --sku Basic \
        --admin-enabled true \
        --output none
fi

ACR_USERNAME=$(azq acr credential show --name "$ACR_NAME" --query "username" -o tsv)
ACR_PASSWORD=$(azq acr credential show --name "$ACR_NAME" --query "passwords[0].value" -o tsv)

# ---------------------------------------------------------------------------
# Step 3: Storage Account + Blob Container
# ---------------------------------------------------------------------------
echo ">> Step 3: Storage account ($STORAGE_ACCOUNT)..."
if az storage account show --name "$STORAGE_ACCOUNT" --resource-group "$RESOURCE_GROUP" --output none 2>/dev/null; then
    echo "   Already exists, skipping."
else
    az storage account create \
        --resource-group "$RESOURCE_GROUP" \
        --name "$STORAGE_ACCOUNT" \
        --location "$LOCATION" \
        --sku Standard_LRS \
        --output none
fi

BLOB_CONN_STRING=$(azq storage account show-connection-string \
    --resource-group "$RESOURCE_GROUP" \
    --name "$STORAGE_ACCOUNT" \
    --query "connectionString" -o tsv)

echo "   Creating blob container ($BLOB_CONTAINER)..."
az storage container create \
    --name "$BLOB_CONTAINER" \
    --account-name "$STORAGE_ACCOUNT" \
    --output none

# ---------------------------------------------------------------------------
# Step 4: Key Vault + Secrets
# ---------------------------------------------------------------------------
echo ">> Step 4: Key Vault ($KEYVAULT_NAME)..."
if az keyvault show --name "$KEYVAULT_NAME" --output none 2>/dev/null; then
    echo "   Already exists, skipping creation."
else
    # Recover soft-deleted vault if it exists (from a previous teardown)
    if az keyvault show-deleted --name "$KEYVAULT_NAME" --output none 2>/dev/null; then
        echo "   Purging soft-deleted vault from previous deployment..."
        az keyvault purge --name "$KEYVAULT_NAME" --output none
    fi

    az keyvault create \
        --resource-group "$RESOURCE_GROUP" \
        --name "$KEYVAULT_NAME" \
        --location "$LOCATION" \
        --enable-rbac-authorization true \
        --output none
fi

KEYVAULT_ID=$(azq keyvault show --name "$KEYVAULT_NAME" --query "id" -o tsv)
KEYVAULT_URI=$(azq keyvault show --name "$KEYVAULT_NAME" --query "properties.vaultUri" -o tsv | sed 's:/$::')

# Grant the current user permission to set secrets
CURRENT_USER_ID=$(azq ad signed-in-user show --query "id" -o tsv)
az role assignment create \
    --role "Key Vault Secrets Officer" \
    --assignee-object-id "$CURRENT_USER_ID" \
    --assignee-principal-type User \
    --scope "$KEYVAULT_ID" \
    --output none 2>/dev/null || echo "   Role assignment already exists, skipping."

echo "   Setting secrets (with retry for RBAC propagation)..."
retry 6 10 az keyvault secret set --vault-name "$KEYVAULT_NAME" --name "GeminiApiKey" --value "$GEMINI_API_KEY" --output none
retry 6 10 az keyvault secret set --vault-name "$KEYVAULT_NAME" --name "AzureBlobConnectionString" --value "$BLOB_CONN_STRING" --output none

# ---------------------------------------------------------------------------
# Step 5: Container Apps Environment
# ---------------------------------------------------------------------------
echo ">> Step 5: Container Apps environment ($ENVIRONMENT_NAME)..."
if az containerapp env show --name "$ENVIRONMENT_NAME" --resource-group "$RESOURCE_GROUP" --output none 2>/dev/null; then
    echo "   Already exists, skipping."
else
    az containerapp env create \
        --resource-group "$RESOURCE_GROUP" \
        --name "$ENVIRONMENT_NAME" \
        --location "$LOCATION" \
        --output none
fi

# ---------------------------------------------------------------------------
# Step 6: Determine next version + build & push Docker images
# ---------------------------------------------------------------------------
# Returns the highest existing v0.0.N tag in a single repo, as a bare integer.
# Returns 0 if the repo doesn't exist yet or has no matching tags.
get_max_version_in_repo() {
    local repo=$1
    local tags
    tags=$(azq acr repository show-tags --name "$ACR_NAME" --repository "$repo" --output tsv 2>/dev/null || true)

    if [[ -z "$tags" ]]; then
        echo "0"
        return
    fi

    local max
    max=$(echo "$tags" | grep -E '^v0\.0\.[0-9]+$' | sed 's/^v0\.0\.//' | sort -n | tail -1 || true)
    echo "${max:-0}"
}

echo ">> Step 6: Determining next image version..."
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
echo "   Next version: $IMAGE_TAG (will also retag as :latest)"

echo "   Building backend ($IMAGE_TAG)..."
az acr build \
    --registry "$ACR_NAME" \
    --image "lt-backend:${IMAGE_TAG}" \
    --image "lt-backend:latest" \
    --file "$WIN_PROJECT_ROOT/app/backend/Dockerfile" \
    "$WIN_PROJECT_ROOT/app/"

echo "   Building worker ($IMAGE_TAG)..."
az acr build \
    --registry "$ACR_NAME" \
    --image "lt-worker:${IMAGE_TAG}" \
    --image "lt-worker:latest" \
    --file "$WIN_PROJECT_ROOT/app/worker/Dockerfile" \
    "$WIN_PROJECT_ROOT/app/"

echo "   Building frontend ($IMAGE_TAG)..."
az acr build \
    --registry "$ACR_NAME" \
    --image "lt-frontend:${IMAGE_TAG}" \
    --image "lt-frontend:latest" \
    --file "$WIN_PROJECT_ROOT/app/frontend/Dockerfile" \
    --build-arg "VITE_GOOGLE_CLIENT_ID=$GOOGLE_CLIENT_ID" \
    "$WIN_PROJECT_ROOT/app/frontend/"

# ---------------------------------------------------------------------------
# Step 7: Deploy Backend (internal ingress)
# ---------------------------------------------------------------------------
echo ">> Step 7: Deploying backend..."

# Split allowed users into indexed env vars
IFS=',' read -ra USERS <<< "$ALLOWED_USERS"
USER_ENV_VARS=()
for i in "${!USERS[@]}"; do
    USER_ENV_VARS+=("AllowedUsers__${i}=${USERS[$i]}")
done

BACKEND_IMAGE="${ACR_NAME}.azurecr.io/lt-backend:${IMAGE_TAG}"

if az containerapp show --name "$BACKEND_APP" --resource-group "$RESOURCE_GROUP" --output none 2>/dev/null; then
    echo "   Backend app exists — updating to $IMAGE_TAG..."
    az containerapp update \
        --name "$BACKEND_APP" \
        --resource-group "$RESOURCE_GROUP" \
        --image "$BACKEND_IMAGE" \
        --set-env-vars \
            ASPNETCORE_ENVIRONMENT=Production \
            StorageProvider=AzureBlob \
            "AzureBlob__ContainerName=$BLOB_CONTAINER" \
            "Gemini__Model=$GEMINI_MODEL" \
            "Authentication__Google__ClientId=$GOOGLE_CLIENT_ID" \
            "${USER_ENV_VARS[@]}" \
        --output none
else
    echo "   Creating backend app..."
    az containerapp create \
        --name "$BACKEND_APP" \
        --resource-group "$RESOURCE_GROUP" \
        --environment "$ENVIRONMENT_NAME" \
        --image "$BACKEND_IMAGE" \
        --registry-server "${ACR_NAME}.azurecr.io" \
        --registry-username "$ACR_USERNAME" \
        --registry-password "$ACR_PASSWORD" \
        --target-port 8080 \
        --ingress internal \
        --allow-insecure \
        --min-replicas 1 \
        --max-replicas 1 \
        --env-vars \
            ASPNETCORE_ENVIRONMENT=Production \
            StorageProvider=AzureBlob \
            "AzureBlob__ContainerName=$BLOB_CONTAINER" \
            "Gemini__Model=$GEMINI_MODEL" \
            "Authentication__Google__ClientId=$GOOGLE_CLIENT_ID" \
            "${USER_ENV_VARS[@]}" \
        --output none
fi

# Enable managed identity
az containerapp identity assign \
    --name "$BACKEND_APP" \
    --resource-group "$RESOURCE_GROUP" \
    --system-assigned \
    --output none

BACKEND_IDENTITY=$(azq containerapp show \
    --name "$BACKEND_APP" \
    --resource-group "$RESOURCE_GROUP" \
    --query "identity.principalId" -o tsv)

# Grant Key Vault access to backend identity
az role assignment create \
    --role "Key Vault Secrets User" \
    --assignee-object-id "$BACKEND_IDENTITY" \
    --assignee-principal-type ServicePrincipal \
    --scope "$KEYVAULT_ID" \
    --output none 2>/dev/null || echo "   Role assignment already exists, skipping."

# Brief wait for identity role propagation, then add KV secret references
echo "   Configuring Key Vault secret references..."
sleep 15

az containerapp secret set \
    --name "$BACKEND_APP" \
    --resource-group "$RESOURCE_GROUP" \
    --secrets \
        "gemini-api-key=keyvaultref:${KEYVAULT_URI}/secrets/GeminiApiKey,identityref:system" \
        "blob-conn-string=keyvaultref:${KEYVAULT_URI}/secrets/AzureBlobConnectionString,identityref:system" \
    --output none

# Update env vars to reference secrets
az containerapp update \
    --name "$BACKEND_APP" \
    --resource-group "$RESOURCE_GROUP" \
    --set-env-vars \
        "Gemini__ApiKey=secretref:gemini-api-key" \
        "AzureBlob__ConnectionString=secretref:blob-conn-string" \
    --output none

# ---------------------------------------------------------------------------
# Step 8: Deploy Frontend (external ingress)
# ---------------------------------------------------------------------------
echo ">> Step 8: Deploying frontend..."

BACKEND_FQDN=$(azq containerapp show \
    --name "$BACKEND_APP" \
    --resource-group "$RESOURCE_GROUP" \
    --query "properties.configuration.ingress.fqdn" -o tsv)

FRONTEND_IMAGE="${ACR_NAME}.azurecr.io/lt-frontend:${IMAGE_TAG}"

if az containerapp show --name "$FRONTEND_APP" --resource-group "$RESOURCE_GROUP" --output none 2>/dev/null; then
    echo "   Frontend app exists — updating to $IMAGE_TAG..."
    az containerapp update \
        --name "$FRONTEND_APP" \
        --resource-group "$RESOURCE_GROUP" \
        --image "$FRONTEND_IMAGE" \
        --set-env-vars \
            "BACKEND_HOST=$BACKEND_FQDN" \
            "BACKEND_PORT=80" \
            "BACKEND_SCHEME=http" \
        --output none
else
    echo "   Creating frontend app..."
    az containerapp create \
        --name "$FRONTEND_APP" \
        --resource-group "$RESOURCE_GROUP" \
        --environment "$ENVIRONMENT_NAME" \
        --image "$FRONTEND_IMAGE" \
        --registry-server "${ACR_NAME}.azurecr.io" \
        --registry-username "$ACR_USERNAME" \
        --registry-password "$ACR_PASSWORD" \
        --target-port 80 \
        --ingress external \
        --min-replicas 1 \
        --max-replicas 1 \
        --env-vars \
            "BACKEND_HOST=$BACKEND_FQDN" \
            "BACKEND_PORT=80" \
            "BACKEND_SCHEME=http" \
        --output none
fi

# ---------------------------------------------------------------------------
# Step 9: Deploy Worker as Scheduled Job
# ---------------------------------------------------------------------------
echo ">> Step 9: Deploying worker job..."

WORKER_IMAGE="${ACR_NAME}.azurecr.io/lt-worker:${IMAGE_TAG}"

if az containerapp job show --name "$WORKER_JOB" --resource-group "$RESOURCE_GROUP" --output none 2>/dev/null; then
    echo "   Worker job exists — will be updated via YAML below with $IMAGE_TAG"
else
    echo "   Creating worker job..."
    az containerapp job create \
        --name "$WORKER_JOB" \
        --resource-group "$RESOURCE_GROUP" \
        --environment "$ENVIRONMENT_NAME" \
        --image "$WORKER_IMAGE" \
        --registry-server "${ACR_NAME}.azurecr.io" \
        --registry-username "$ACR_USERNAME" \
        --registry-password "$ACR_PASSWORD" \
        --trigger-type Schedule \
        --cron-expression "*/5 * * * *" \
        --replica-timeout 300 \
        --parallelism 1 \
        --replica-retry-limit 0 \
        --env-vars \
            DOTNET_ENVIRONMENT=Production \
            StorageProvider=AzureBlob \
            "AzureBlob__ContainerName=$BLOB_CONTAINER" \
            "Gemini__Model=$GEMINI_MODEL" \
        --output none
fi

# Enable managed identity on worker job
az containerapp job identity assign \
    --name "$WORKER_JOB" \
    --resource-group "$RESOURCE_GROUP" \
    --system-assigned \
    --output none

WORKER_IDENTITY=$(azq containerapp job show \
    --name "$WORKER_JOB" \
    --resource-group "$RESOURCE_GROUP" \
    --query "identity.principalId" -o tsv)

# Grant Key Vault access to worker identity
az role assignment create \
    --role "Key Vault Secrets User" \
    --assignee-object-id "$WORKER_IDENTITY" \
    --assignee-principal-type ServicePrincipal \
    --scope "$KEYVAULT_ID" \
    --output none 2>/dev/null || echo "   Role assignment already exists, skipping."

echo "   Configuring Key Vault secret references..."
sleep 15

# Use YAML-based update for worker job secrets + env vars (more reliable
# than az containerapp job secret set which may not exist in all CLI versions)
WORKER_YAML_UNIX="${PROJECT_ROOT}/infra/worker-yaml-tmp.yml"
WORKER_YAML_WIN="${WIN_PROJECT_ROOT}/infra/worker-yaml-tmp.yml"
ACR_SECRET_NAME="${ACR_NAME}azurecrio-${ACR_NAME}"
cat > "$WORKER_YAML_UNIX" <<YAML
properties:
  configuration:
    secrets:
      - name: ${ACR_SECRET_NAME}
        value: ${ACR_PASSWORD}
      - name: gemini-api-key
        keyVaultUrl: ${KEYVAULT_URI}/secrets/GeminiApiKey
        identity: system
      - name: blob-conn-string
        keyVaultUrl: ${KEYVAULT_URI}/secrets/AzureBlobConnectionString
        identity: system
    registries:
      - server: ${ACR_NAME}.azurecr.io
        username: ${ACR_USERNAME}
        passwordSecretRef: ${ACR_SECRET_NAME}
    triggerType: Schedule
    scheduleTriggerConfig:
      cronExpression: "*/5 * * * *"
      parallelism: 1
      replicaCompletionCount: 1
    replicaTimeout: 300
  template:
    containers:
      - name: ${WORKER_JOB}
        image: ${ACR_NAME}.azurecr.io/lt-worker:${IMAGE_TAG}
        env:
          - name: DOTNET_ENVIRONMENT
            value: Production
          - name: StorageProvider
            value: AzureBlob
          - name: AzureBlob__ContainerName
            value: ${BLOB_CONTAINER}
          - name: Gemini__Model
            value: ${GEMINI_MODEL}
          - name: Gemini__ApiKey
            secretRef: gemini-api-key
          - name: AzureBlob__ConnectionString
            secretRef: blob-conn-string
YAML

az containerapp job update \
    --name "$WORKER_JOB" \
    --resource-group "$RESOURCE_GROUP" \
    --yaml "$WORKER_YAML_WIN" \
    --output none

rm -f "$WORKER_YAML_UNIX"

# ---------------------------------------------------------------------------
# Step 10: Output
# ---------------------------------------------------------------------------
FRONTEND_FQDN=$(azq containerapp show \
    --name "$FRONTEND_APP" \
    --resource-group "$RESOURCE_GROUP" \
    --query "properties.configuration.ingress.fqdn" -o tsv)

echo ""
echo "============================================"
echo "Deployment complete!  Version: $IMAGE_TAG"
echo "============================================"
echo ""
echo "Frontend URL:  https://$FRONTEND_FQDN"
echo "Image tag:     $IMAGE_TAG  (also tagged :latest)"
echo ""
echo "NEXT STEPS:"
echo "  1. Go to Google Cloud Console > APIs & Services > Credentials"
echo "  2. Add https://$FRONTEND_FQDN to:"
echo "     - Authorized JavaScript origins"
echo "     - Authorized redirect URIs"
echo "  3. Visit https://$FRONTEND_FQDN and log in"
echo ""
echo "Useful commands:"
echo "  az containerapp logs show -n $BACKEND_APP -g $RESOURCE_GROUP --follow"
echo "  az containerapp logs show -n $FRONTEND_APP -g $RESOURCE_GROUP --follow"
echo "  az containerapp job execution list -n $WORKER_JOB -g $RESOURCE_GROUP"
echo ""
