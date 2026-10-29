variable "location" {
  type    = string
  default = "westus3"
}

variable "app_service_sku" {
  type = string
}

# No default and not in terraform.tfvars: supply it as TF_VAR_spotify_client_id
# (an environment variable), so each fork or CI run brings its own.
variable "spotify_client_id" {
  type = string
}

variable "callback_uri" {
  type    = string
  default = null
}
