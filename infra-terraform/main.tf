# ============================================================================
# main.tf — Letter Translation infrastructure.
#
# Resources here mirror what `infra/deploy.sh` creates. The image build itself
# (az acr build) is NOT owned by Terraform — see README for the release flow.
# ============================================================================

data "azurerm_client_config" "current" {}

# ---------------------------------------------------------------------------
# Resource group
# ---------------------------------------------------------------------------
resource "azurerm_resource_group" "main" {
  name     = local.resource_group_name
  location = var.location
}

# ---------------------------------------------------------------------------
# Container Registry
# ---------------------------------------------------------------------------
resource "azurerm_container_registry" "main" {
  name                = local.acr_name
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  sku                 = "Basic"
  admin_enabled       = true
}

# ---------------------------------------------------------------------------
# Storage account + blob container (where job data lives)
# ---------------------------------------------------------------------------
resource "azurerm_storage_account" "main" {
  name                     = local.storage_account
  resource_group_name      = azurerm_resource_group.main.name
  location                 = azurerm_resource_group.main.location
  account_tier             = "Standard"
  account_replication_type = "LRS"

  # Match what `az storage account create` produced. TODO: consider raising
  # min_tls_version to TLS1_2 and lowering allow_nested_items_to_be_public
  # in a follow-up — but do it as an explicit change, not a "drifted from
  # default" surprise.
  min_tls_version                 = "TLS1_0"
  allow_nested_items_to_be_public = false
}

resource "azurerm_storage_container" "data" {
  name                  = local.blob_container
  storage_account_id    = azurerm_storage_account.main.id
  container_access_type = "private"
}

# ---------------------------------------------------------------------------
# Key Vault + secrets
# ---------------------------------------------------------------------------
resource "azurerm_key_vault" "main" {
  name                       = local.keyvault_name
  resource_group_name        = azurerm_resource_group.main.name
  location                   = azurerm_resource_group.main.location
  tenant_id                  = data.azurerm_client_config.current.tenant_id
  sku_name                   = "standard"
  rbac_authorization_enabled = true
  soft_delete_retention_days = 90
}

# The current user/SP needs Secrets Officer to set values below.
resource "azurerm_role_assignment" "kv_current_user" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = data.azurerm_client_config.current.object_id
}

resource "azurerm_key_vault_secret" "gemini_api_key" {
  name         = "GeminiApiKey"
  value        = var.gemini_api_key
  key_vault_id = azurerm_key_vault.main.id

  # `az keyvault secret set` stamps this tag automatically; match it.
  tags = {
    "file-encoding" = "utf-8"
  }

  depends_on = [azurerm_role_assignment.kv_current_user]
}

resource "azurerm_key_vault_secret" "blob_conn_string" {
  name         = "AzureBlobConnectionString"
  value        = azurerm_storage_account.main.primary_connection_string
  key_vault_id = azurerm_key_vault.main.id

  tags = {
    "file-encoding" = "utf-8"
  }

  # `value` is computed from the storage account's primary_connection_string,
  # which embeds the (rotating) account key. TF would otherwise re-write the
  # secret on every plan. The actual conn string was set by the original
  # deploy.sh and lives in KV — leave that as the source of truth.
  lifecycle {
    ignore_changes = [value]
  }

  depends_on = [azurerm_role_assignment.kv_current_user]
}

# ---------------------------------------------------------------------------
# Container Apps environment
# ---------------------------------------------------------------------------
resource "azurerm_container_app_environment" "main" {
  name                = local.environment_name
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location

  # Azure auto-creates a Log Analytics workspace + a Consumption workload
  # profile when the env is created via `az containerapp env create`. We
  # don't want to manage those here; ignore_changes leaves them untouched.
  lifecycle {
    ignore_changes = [
      log_analytics_workspace_id,
      workload_profile,
    ]
  }
}

# ---------------------------------------------------------------------------
# Backend container app (internal ingress)
# ---------------------------------------------------------------------------
resource "azurerm_container_app" "backend" {
  name                         = local.backend_app
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = azurerm_resource_group.main.name
  revision_mode                = "Single"
  workload_profile_name        = "Consumption"

  identity {
    type = "SystemAssigned"
  }

  registry {
    server               = azurerm_container_registry.main.login_server
    username             = azurerm_container_registry.main.admin_username
    password_secret_name = local.registry_password_secret
  }

  secret {
    name  = local.registry_password_secret
    value = azurerm_container_registry.main.admin_password
  }

  secret {
    name                = "gemini-api-key"
    identity            = "System"
    key_vault_secret_id = azurerm_key_vault_secret.gemini_api_key.versionless_id
  }

  secret {
    name                = "blob-conn-string"
    identity            = "System"
    key_vault_secret_id = azurerm_key_vault_secret.blob_conn_string.versionless_id
  }

  ingress {
    external_enabled           = false
    target_port                = 8080
    allow_insecure_connections = true
    # transport defaults to "auto" — match what `az containerapp create` produces.

    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }

  template {
    min_replicas = 1
    max_replicas = 1

    container {
      name   = "lt-backend"
      image  = local.backend_image
      cpu    = 0.5
      memory = "1Gi"

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }
      env {
        name  = "StorageProvider"
        value = "AzureBlob"
      }
      env {
        name  = "AzureBlob__ContainerName"
        value = local.blob_container
      }
      env {
        name  = "Gemini__Model"
        value = var.gemini_model
      }
      env {
        name  = "Authentication__Google__ClientId"
        value = var.google_client_id
      }

      # AllowedUsers__N come BEFORE the KV secret refs (matches what
      # deploy.sh wrote — order is significant in this provider).
      dynamic "env" {
        for_each = local.allowed_users_env
        content {
          name  = env.key
          value = env.value
        }
      }

      env {
        name        = "Gemini__ApiKey"
        secret_name = "gemini-api-key"
      }
      env {
        name        = "AzureBlob__ConnectionString"
        secret_name = "blob-conn-string"
      }
    }
  }

  # Azure auto-attaches a default http-scaler when ingress is enabled;
  # we don't manage it here. The secret blocks include KV references whose
  # contents the provider can't reliably diff (sensitivity + identity ref),
  # so we ignore them post-import.
  lifecycle {
    ignore_changes = [
      template[0].http_scale_rule,
      secret,
    ]
  }
}

# Backend identity → Key Vault Secrets User
resource "azurerm_role_assignment" "backend_kv" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_container_app.backend.identity[0].principal_id
}

# ---------------------------------------------------------------------------
# Frontend container app (external ingress)
# ---------------------------------------------------------------------------
resource "azurerm_container_app" "frontend" {
  name                         = local.frontend_app
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = azurerm_resource_group.main.name
  revision_mode                = "Single"
  workload_profile_name        = "Consumption"

  registry {
    server               = azurerm_container_registry.main.login_server
    username             = azurerm_container_registry.main.admin_username
    password_secret_name = local.registry_password_secret
  }

  secret {
    name  = local.registry_password_secret
    value = azurerm_container_registry.main.admin_password
  }

  ingress {
    external_enabled = true
    target_port      = 80
    # transport defaults to "auto"

    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }

  template {
    min_replicas = 1
    max_replicas = 1

    container {
      name   = "lt-frontend"
      image  = local.frontend_image
      cpu    = 0.5
      memory = "1Gi"

      env {
        name  = "BACKEND_HOST"
        value = azurerm_container_app.backend.ingress[0].fqdn
      }
      env {
        name  = "BACKEND_PORT"
        value = "80"
      }
      env {
        name  = "BACKEND_SCHEME"
        value = "http"
      }
    }
  }

  # Azure auto-creates a default http-scaler and TCP probes when an
  # external ingress container app is provisioned; we don't manage
  # those here. A leftover "test" env var on the running revision
  # also lives outside our config — letting plan show that single
  # diff is intentional for now.
  lifecycle {
    ignore_changes = [
      template[0].http_scale_rule,
      template[0].container[0].liveness_probe,
      template[0].container[0].readiness_probe,
      template[0].container[0].startup_probe,
    ]
  }
}

# ---------------------------------------------------------------------------
# Worker container app job (scheduled, cron every 5 minutes)
# ---------------------------------------------------------------------------
resource "azurerm_container_app_job" "worker" {
  name                         = local.worker_job
  resource_group_name          = azurerm_resource_group.main.name
  location                     = azurerm_resource_group.main.location
  container_app_environment_id = azurerm_container_app_environment.main.id
  workload_profile_name        = "Consumption"

  replica_timeout_in_seconds = 300
  replica_retry_limit        = 0

  schedule_trigger_config {
    cron_expression          = "*/5 * * * *"
    parallelism              = 1
    replica_completion_count = 1
  }

  identity {
    type = "SystemAssigned"
  }

  registry {
    server               = azurerm_container_registry.main.login_server
    username             = azurerm_container_registry.main.admin_username
    password_secret_name = local.registry_password_secret
  }

  secret {
    name  = local.registry_password_secret
    value = azurerm_container_registry.main.admin_password
  }

  secret {
    name                = "gemini-api-key"
    identity            = "System"
    key_vault_secret_id = azurerm_key_vault_secret.gemini_api_key.versionless_id
  }

  secret {
    name                = "blob-conn-string"
    identity            = "System"
    key_vault_secret_id = azurerm_key_vault_secret.blob_conn_string.versionless_id
  }

  template {
    container {
      name   = "lt-worker"
      image  = local.worker_image
      cpu    = 0.5
      memory = "1Gi"

      env {
        name  = "DOTNET_ENVIRONMENT"
        value = "Production"
      }
      env {
        name  = "StorageProvider"
        value = "AzureBlob"
      }
      env {
        name  = "AzureBlob__ContainerName"
        value = local.blob_container
      }
      env {
        name  = "Gemini__Model"
        value = var.gemini_model
      }
      env {
        name        = "Gemini__ApiKey"
        secret_name = "gemini-api-key"
      }
      env {
        name        = "AzureBlob__ConnectionString"
        secret_name = "blob-conn-string"
      }
    }
  }

  # Same reasoning as backend: secret blocks contain KV references whose
  # contents the provider can't reliably diff. Once imported they're
  # managed out-of-band.
  lifecycle {
    ignore_changes = [secret]
  }
}

# Worker identity → Key Vault Secrets User
resource "azurerm_role_assignment" "worker_kv" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_container_app_job.worker.identity[0].principal_id
}
