locals {
  # Derived suffix — sha256(subscription_id), first 6 hex chars.
  # Matches the bash expression in deploy.sh:
  #   printf '%s' "$SUB_ID" | openssl dgst -sha256 | cut -c1-6
  suffix = substr(sha256(var.subscription_id), 0, 6)

  resource_group_name = "lt-rg"
  acr_name            = "ltacr${local.suffix}"
  storage_account     = "ltstorage${local.suffix}"
  keyvault_name       = "lt-kv-${local.suffix}"
  environment_name    = "lt-env"
  blob_container      = "letter-translation"

  backend_app  = "lt-backend"
  frontend_app = "lt-frontend"
  worker_job   = "lt-worker"

  # Image references (composed with var.image_tag at apply time)
  backend_image  = "${local.acr_name}.azurecr.io/lt-backend:${var.image_tag}"
  frontend_image = "${local.acr_name}.azurecr.io/lt-frontend:${var.image_tag}"
  worker_image   = "${local.acr_name}.azurecr.io/lt-worker:${var.image_tag}"

  # Azure auto-generates the ACR password secret name when you pass registry
  # creds to `az containerapp create`. Pattern: "<acr>azurecrio-<acr>".
  # Matching this avoids a secret-rename diff on every plan.
  registry_password_secret = "${local.acr_name}azurecrio-${local.acr_name}"

  # Allowed-users CSV -> indexed env-var pairs (AllowedUsers__0, __1, ...)
  allowed_users_list = split(",", var.allowed_users)
  allowed_users_env = {
    for i, email in local.allowed_users_list :
    "AllowedUsers__${i}" => trimspace(email)
  }
}
