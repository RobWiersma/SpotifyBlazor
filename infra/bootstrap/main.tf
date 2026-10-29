# -----------------------------------------------------------------------------
# Bootstrap: the storage account that holds Terraform state for every
# environment.
#
# Chicken-and-egg: remote state has to live somewhere before Terraform can use
# it, so this one small config keeps LOCAL state (terraform.tfstate in this
# folder, git-ignored). Run it once per subscription; everything under envs/
# then stores its state in the container created here.
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
}

provider "azurerm" {
  features {}

  # Talk to blob storage with your Entra ID login instead of account keys
  # (key auth is turned off on the account below).
  storage_use_azuread = true
}

# Whoever runs `apply` (you locally, or the CI service principal).
data "azurerm_client_config" "current" {}

# Storage account names are global across Azure, so add a random suffix.
# The result is stored in state, so it stays the same on every later run.
resource "random_string" "suffix" {
  length  = 6
  upper   = false
  special = false
}

resource "azurerm_resource_group" "tfstate" {
  name     = "rg-spotifyblazor-tfstate"
  location = var.location
  tags     = local.tags
}

resource "azurerm_storage_account" "tfstate" {
  name                     = "stsbtfstate${random_string.suffix.result}"
  resource_group_name      = azurerm_resource_group.tfstate.name
  location                 = azurerm_resource_group.tfstate.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
  min_tls_version          = "TLS1_2"

  # State files can contain sensitive values: no anonymous access and no
  # shared keys. Only Entra ID identities with a data-plane role can read it.
  allow_nested_items_to_be_public = false
  shared_access_key_enabled       = false

  blob_properties {
    # Every state write becomes a new blob version, so a bad apply can be
    # rolled back by restoring an earlier version.
    versioning_enabled = true

    delete_retention_policy {
      days = 14
    }
  }

  tags = local.tags

  # Losing this account means losing track of every resource Terraform
  # manages. Make `destroy` refuse unless this line is removed first.
  lifecycle {
    prevent_destroy = true
  }
}

resource "azurerm_storage_container" "tfstate" {
  name                  = "tfstate"
  storage_account_id    = azurerm_storage_account.tfstate.id
  container_access_type = "private"
}

# Owner/Contributor on the subscription is NOT enough to read blobs when key
# auth is off: give the identity running bootstrap a data-plane role too.
resource "azurerm_role_assignment" "state_blob_contributor" {
  scope                = azurerm_storage_account.tfstate.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = data.azurerm_client_config.current.object_id
}

locals {
  tags = {
    app        = "spotifyblazor"
    managed-by = "terraform"
    purpose    = "terraform-state"
  }
}
