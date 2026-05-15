#!/usr/bin/env bash
set -euo pipefail

# ============================================================================
# teardown.sh — Delete all Azure resources for Letter Translation
#
# Deletes the resource group, which cascades to every resource inside it.
# ============================================================================

RESOURCE_GROUP="lt-rg"

# Derive Key Vault name (same logic as deploy.sh)
SUB_ID=$(az account show --query "id" -o tsv)
SUFFIX=$(printf '%s' "$SUB_ID" | openssl dgst -sha256 2>/dev/null | sed 's/.*= //' | cut -c1-6)
KEYVAULT_NAME="lt-kv-${SUFFIX}"

echo "This will delete resource group '$RESOURCE_GROUP' and ALL resources inside it."
read -rp "Are you sure? (y/N): " confirm
if [[ "$confirm" != "y" && "$confirm" != "Y" ]]; then
    echo "Aborted."
    exit 0
fi

echo "Deleting resource group '$RESOURCE_GROUP'..."
az group delete --name "$RESOURCE_GROUP" --yes

# Purge the soft-deleted Key Vault so the name can be reused on next deploy
echo "Purging soft-deleted Key Vault ($KEYVAULT_NAME)..."
az keyvault purge --name "$KEYVAULT_NAME" --output none 2>/dev/null || true

echo ""
echo "Resource group deleted and Key Vault purged."
echo "You can redeploy from scratch with: ./deploy.sh"
