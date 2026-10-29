# A module declares which providers it needs, but never configures them:
# the calling root module (envs/prod) owns the `provider` block,
# so credentials and subscription stay out of reusable code.
terraform {
  required_version = ">= 1.9"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }
}
