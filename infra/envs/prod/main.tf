# -----------------------------------------------------------------------------
# Root module for prod, the only hosted environment (dev runs locally with
# `dotnet run`). It stays thin: configure the provider and backend, then call
# the shared module. A hosted staging/test environment would be a sibling
# folder (envs/test) with its own state file, calling the same module.
# -----------------------------------------------------------------------------

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

  # Remote state in the storage account made by infra/bootstrap. The account
  # name is left out on purpose ("partial configuration") and passed at init:
  #   terraform init -backend-config="storage_account_name=<name>"
  # Backend blocks can't use variables, because they're read before
  # variables exist.
  backend "azurerm" {
    resource_group_name = "rg-spotifyblazor-tfstate"
    container_name      = "tfstate"
    key                 = "spotifyblazor/prod.tfstate"
    use_azuread_auth    = true
  }
}

# Subscription and credentials come from the environment, never from code:
# locally `az login` + ARM_SUBSCRIPTION_ID; in CI the OIDC ARM_* variables.
provider "azurerm" {
  features {
    key_vault {
      # A destroyed vault stays soft-deleted (recoverable) instead of purged.
      purge_soft_delete_on_destroy = false
    }
  }
}

module "app" {
  source = "../../modules/spotify-app"

  environment       = "prod"
  location          = var.location
  app_service_sku   = var.app_service_sku
  spotify_client_id = var.spotify_client_id
  callback_uri      = var.callback_uri
}
