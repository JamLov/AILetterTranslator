terraform {
  required_version = ">= 1.5"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
  }

  # All backend args (resource_group_name, storage_account_name,
  # container_name, key) come from backend.tfvars at init time:
  #   terraform init -backend-config=backend.tfvars
  backend "azurerm" {}
}

provider "azurerm" {
  subscription_id = var.subscription_id

  features {
    key_vault {
      # Match deploy.sh — recover/purge soft-deleted vaults so the name
      # can be reused after a teardown.
      purge_soft_delete_on_destroy    = true
      recover_soft_deleted_key_vaults = true
    }
    resource_group {
      prevent_deletion_if_contains_resources = false
    }
  }
}
