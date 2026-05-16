# ============================================================================
# imports.tf — declarative imports of the existing Azure resources.
#
# Terraform 1.5+ supports `import { to = ... id = ... }` blocks evaluated at
# plan time. After a successful `terraform apply` that absorbs these imports,
# you can DELETE this file — the resources are then in state and managed by
# their `resource` blocks in main.tf.
#
# If you've already torn down (e.g. ran teardown.sh) and there's nothing to
# import, delete this file before the first plan to avoid spurious errors.
# ============================================================================

locals {
  sub_scope = "/subscriptions/${var.subscription_id}"
  rg_scope  = "${local.sub_scope}/resourceGroups/${local.resource_group_name}"
}

import {
  to = azurerm_resource_group.main
  id = local.rg_scope
}

import {
  to = azurerm_container_registry.main
  id = "${local.rg_scope}/providers/Microsoft.ContainerRegistry/registries/${local.acr_name}"
}

import {
  to = azurerm_storage_account.main
  id = "${local.rg_scope}/providers/Microsoft.Storage/storageAccounts/${local.storage_account}"
}

import {
  to = azurerm_storage_container.data
  id = "https://${local.storage_account}.blob.core.windows.net/${local.blob_container}"
}

import {
  to = azurerm_key_vault.main
  id = "${local.rg_scope}/providers/Microsoft.KeyVault/vaults/${local.keyvault_name}"
}

import {
  to = azurerm_key_vault_secret.gemini_api_key
  id = "https://${local.keyvault_name}.vault.azure.net/secrets/GeminiApiKey"
}

import {
  to = azurerm_key_vault_secret.blob_conn_string
  id = "https://${local.keyvault_name}.vault.azure.net/secrets/AzureBlobConnectionString"
}

import {
  to = azurerm_container_app_environment.main
  id = "${local.rg_scope}/providers/Microsoft.App/managedEnvironments/${local.environment_name}"
}

import {
  to = azurerm_container_app.backend
  id = "${local.rg_scope}/providers/Microsoft.App/containerApps/${local.backend_app}"
}

import {
  to = azurerm_container_app.frontend
  id = "${local.rg_scope}/providers/Microsoft.App/containerApps/${local.frontend_app}"
}

import {
  to = azurerm_container_app_job.worker
  id = "${local.rg_scope}/providers/Microsoft.App/jobs/${local.worker_job}"
}

# Role assignments don't have stable, predictable IDs — they're GUIDs that
# the platform assigns. The simplest path is to NOT import them and let
# Terraform create new role assignments (Azure tolerates duplicates with
# matching principal+scope+role). The CLI-created ones can be cleaned up
# afterwards if you want a tidy state.
