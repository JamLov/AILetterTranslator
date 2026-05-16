variable "subscription_id" {
  description = "Azure subscription ID. Used to derive resource name suffix (must match deploy.sh)."
  type        = string
}

variable "location" {
  type    = string
  default = "uksouth"
}

variable "image_tag" {
  description = "Container image tag (e.g. v0.0.5). For an initial import use 'latest'; subsequent releases set this per-deploy from CI."
  type        = string
  default     = "latest"
}

variable "google_client_id" {
  description = "Google OAuth client ID (also baked into frontend at build time, separate var there)."
  type        = string
  sensitive   = true
}

variable "allowed_users" {
  description = "Comma-separated list of permitted Google account emails."
  type        = string
}

variable "gemini_api_key" {
  description = "Gemini API key. Stored in Key Vault."
  type        = string
  sensitive   = true
}

variable "gemini_model" {
  type    = string
  default = "gemini-2.5-pro"
}
