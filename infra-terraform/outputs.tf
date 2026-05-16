output "frontend_url" {
  description = "Public URL of the frontend container app."
  value       = "https://${azurerm_container_app.frontend.ingress[0].fqdn}"
}

output "backend_fqdn" {
  description = "Internal FQDN of the backend container app."
  value       = azurerm_container_app.backend.ingress[0].fqdn
}

output "acr_login_server" {
  description = "ACR login server (for the build wrapper to push to)."
  value       = azurerm_container_registry.main.login_server
}

output "deployed_image_tag" {
  description = "The image tag currently deployed (last apply input)."
  value       = var.image_tag
}

output "keyvault_uri" {
  value = azurerm_key_vault.main.vault_uri
}
