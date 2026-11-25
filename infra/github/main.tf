# -----------------------------------------------------------------------------
# GitHub Actions' access to Azure, with OIDC (workload identity federation).
# Entra ID trusts tokens that GitHub issues to this repository's workflows, so
# there's no client secret to store, leak or rotate.
#
# Two identities, each with the least it needs:
#   deploy: deploys the live web app. Trusted only for jobs that run in the
#           GitHub "production" environment, so a pull request can't deploy.
#   plan:   the read-only `terraform plan` in terraform.yml. Trusted for pull
#           requests and pushes to main.
#
# Apply it from a laptop (`az login` as someone who can create app
# registrations and assign roles), then copy the outputs into GitHub's
# repository variables. CI never applies this.
# -----------------------------------------------------------------------------

terraform {
  required_version = ">= 1.9"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.0"
    }
    azuread = {
      source  = "hashicorp/azuread"
      version = "~> 3.0"
    }
  }

  # Same state account as prod, its own state file:
  #   terraform init -backend-config="storage_account_name=<name>"
  backend "azurerm" {
    resource_group_name = "rg-spotifyblazor-tfstate"
    container_name      = "tfstate"
    key                 = "spotifyblazor/github.tfstate"
    use_azuread_auth    = true
  }
}

provider "azurerm" {
  features {}
}

provider "azuread" {}

data "azurerm_client_config" "current" {}

data "azurerm_subscription" "current" {}

# The live site, made by hand and published from Visual Studio until now. It's
# only read here (for its id); Terraform doesn't manage it.
data "azurerm_windows_web_app" "live" {
  name                = var.live_web_app_name
  resource_group_name = var.live_resource_group_name
}

data "azurerm_storage_account" "tfstate" {
  name                = var.tfstate_storage_account
  resource_group_name = "rg-spotifyblazor-tfstate"
}

locals {
  issuer   = "https://token.actions.githubusercontent.com"
  audience = "api://AzureADTokenExchange"
  repo     = var.github_subject_prefix
}

# -----------------------------------------------------------------------------
# Deploy identity
# -----------------------------------------------------------------------------

resource "azuread_application" "deploy" {
  display_name = "github-spotifyblazor-deploy"
  owners       = [data.azurerm_client_config.current.object_id]
}

resource "azuread_service_principal" "deploy" {
  client_id = azuread_application.deploy.client_id
  owners    = [data.azurerm_client_config.current.object_id]
}

# The token's subject names the environment when a job sets `environment:`,
# so only jobs in "production" match. Required reviewers or branch rules on
# that environment in GitHub then guard every deploy.
resource "azuread_application_federated_identity_credential" "deploy_production" {
  application_id = azuread_application.deploy.id
  display_name   = "production-environment"
  description    = "GitHub Actions jobs in the production environment of ${var.github_repository}"
  issuer         = local.issuer
  audiences      = [local.audience]
  subject        = "${local.repo}:environment:production"
}

# Website Contributor on this one web app: enough to deploy code and restart it,
# nothing else in the subscription.
resource "azurerm_role_assignment" "deploy_web_app" {
  scope                = data.azurerm_windows_web_app.live.id
  role_definition_name = "Website Contributor"
  principal_id         = azuread_service_principal.deploy.object_id
}

# -----------------------------------------------------------------------------
# Plan identity (read-only)
# -----------------------------------------------------------------------------

resource "azuread_application" "plan" {
  display_name = "github-spotifyblazor-terraform"
  owners       = [data.azurerm_client_config.current.object_id]
}

resource "azuread_service_principal" "plan" {
  client_id = azuread_application.plan.client_id
  owners    = [data.azurerm_client_config.current.object_id]
}

resource "azuread_application_federated_identity_credential" "plan" {
  for_each = {
    pull-requests = "pull_request"
    main          = "ref:refs/heads/main"
  }

  application_id = azuread_application.plan.id
  display_name   = each.key
  description    = "terraform plan from GitHub Actions (${each.key})"
  issuer         = local.issuer
  audiences      = [local.audience]
  subject        = "${local.repo}:${each.value}"
}

# Plan reads every resource; reading web app settings needs Website Contributor.
resource "azurerm_role_assignment" "plan" {
  for_each = toset(["Reader", "Website Contributor"])

  scope                = data.azurerm_subscription.current.id
  role_definition_name = each.value
  principal_id         = azuread_service_principal.plan.object_id
}

resource "azurerm_role_assignment" "plan_state" {
  scope                = data.azurerm_storage_account.tfstate.id
  role_definition_name = "Storage Blob Data Reader"
  principal_id         = azuread_service_principal.plan.object_id
}
