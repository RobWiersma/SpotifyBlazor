# Inputs are the module's public API: everything that differs between
# environments is a variable, everything else is decided inside the module.

variable "environment" {
  description = "Short environment name, used in resource names and tags (dev, prod)."
  type        = string

  validation {
    condition     = contains(["dev", "prod"], var.environment)
    error_message = "environment must be \"dev\" or \"prod\"."
  }
}

variable "location" {
  description = "Azure region for all resources."
  type        = string
  default     = "westus3"
}

variable "app_service_sku" {
  description = "App Service plan SKU, e.g. F1 (free), B1, P0v3."
  type        = string
  default     = "B1"
}

variable "spotify_client_id" {
  description = "Spotify app client ID. Not a secret (the app serves it from /api/config), but kept out of git so each fork uses its own."
  type        = string
}

variable "callback_uri" {
  description = "Spotify redirect URI. Defaults to https://<web app host>/spotcallback; set it when using a custom domain."
  type        = string
  default     = null
}

variable "log_retention_days" {
  description = "How long Log Analytics keeps Application Insights data."
  type        = number
  default     = 30
}

variable "tags" {
  description = "Extra tags merged onto every resource."
  type        = map(string)
  default     = {}
}
