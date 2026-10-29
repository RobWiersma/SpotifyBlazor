# -----------------------------------------------------------------------------
# One environment of SpotifyBlazor: a hosted Blazor WebAssembly app, so a
# single ASP.NET Core server serves both the WASM client and the /api
# endpoints that hold the Spotify client secret. That needs a real server,
# which is why this is App Service rather than a Static Web App.
#
#   rg-spotifyblazor-<env>
#   ├── Log Analytics workspace ◄── Application Insights
#   ├── Key Vault  (spotify-client-secret, jwt-signing-key)
#   └── App Service plan (Linux) ◄── Linux Web App (.NET 8, managed identity)
# -----------------------------------------------------------------------------

data "azurerm_client_config" "current" {}

# Web app and Key Vault names are globally unique DNS names. A short random
# suffix avoids collisions and is fixed once it is in state.
resource "random_string" "suffix" {
  length  = 4
  upper   = false
  special = false
}

locals {
  name     = "spotifyblazor-${var.environment}"
  app_name = "app-${local.name}-${random_string.suffix.result}"

  site_url     = "https://${local.app_name}.azurewebsites.net"
  callback_uri = coalesce(var.callback_uri, "${local.site_url}/spotcallback")

  # Free/shared tiers can't keep the app loaded between requests.
  always_on = !contains(["F1", "FREE", "D1", "SHARED"], upper(var.app_service_sku))

  tags = merge(var.tags, {
    app         = "spotifyblazor"
    environment = var.environment
    managed-by  = "terraform"
  })
}

resource "azurerm_resource_group" "this" {
  name     = "rg-${local.name}"
  location = var.location
  tags     = local.tags
}

# --- Monitoring ---------------------------------------------------------------

# Application Insights is "workspace-based": its data lives in Log Analytics,
# where you query it with KQL.
resource "azurerm_log_analytics_workspace" "this" {
  name                = "log-${local.name}"
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  sku                 = "PerGB2018"
  retention_in_days   = var.log_retention_days
  tags                = local.tags
}

resource "azurerm_application_insights" "this" {
  name                = "appi-${local.name}"
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  workspace_id        = azurerm_log_analytics_workspace.this.id
  application_type    = "web"
  tags                = local.tags
}

# --- Secrets ------------------------------------------------------------------

# Terraform creates the vault and the access rules, but never the secret
# VALUES: anything Terraform sets ends up in plain text in the state file.
# You put the values in once with `az keyvault secret set` (see README).
resource "azurerm_key_vault" "this" {
  name                = "kv-sb-${var.environment}-${random_string.suffix.result}"
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  tenant_id           = data.azurerm_client_config.current.tenant_id
  sku_name            = "standard"

  # Azure RBAC role assignments instead of the older vault access policies.
  rbac_authorization_enabled = true

  soft_delete_retention_days = 7
  # Purge protection can never be turned off again, so only prod gets it.
  purge_protection_enabled = var.environment == "prod"

  tags = local.tags
}

# Lets whoever runs `apply` (you, or the CI identity) write the secret values.
resource "azurerm_role_assignment" "deployer_secrets_officer" {
  scope                = azurerm_key_vault.this.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = data.azurerm_client_config.current.object_id
}

# Lets the web app's managed identity READ secrets, nothing more.
resource "azurerm_role_assignment" "app_secrets_user" {
  scope                = azurerm_key_vault.this.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_linux_web_app.this.identity[0].principal_id
}

# --- Hosting ------------------------------------------------------------------

resource "azurerm_service_plan" "this" {
  name                = "asp-${local.name}"
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  os_type             = "Linux"
  sku_name            = var.app_service_sku
  tags                = local.tags
}

resource "azurerm_linux_web_app" "this" {
  name                = local.app_name
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  service_plan_id     = azurerm_service_plan.this.id
  https_only          = true

  # System-assigned managed identity: an Entra ID identity tied to this app,
  # with no password for anyone to manage. Key Vault trusts it via RBAC above.
  identity {
    type = "SystemAssigned"
  }

  site_config {
    always_on           = local.always_on
    ftps_state          = "Disabled"
    http2_enabled       = true
    minimum_tls_version = "1.2"

    application_stack {
      dotnet_version = "8.0"
    }
  }

  # On Linux, "__" in a setting name becomes ":" in .NET configuration, so
  # ConnectionStrings__clientId is read as config["ConnectionStrings:clientId"].
  app_settings = {
    "ConnectionStrings__clientId"    = var.spotify_client_id
    "ConnectionStrings__callbackUri" = local.callback_uri

    # Key Vault references: App Service fetches the value at runtime with the
    # managed identity. Only the reference string is in Terraform and state.
    "ConnectionStrings__clientSecret" = "@Microsoft.KeyVault(VaultName=${azurerm_key_vault.this.name};SecretName=spotify-client-secret)"
    "Jwt__Key"                        = "@Microsoft.KeyVault(VaultName=${azurerm_key_vault.this.name};SecretName=jwt-signing-key)"

    "Jwt__Issuer"   = local.site_url
    "Jwt__Audience" = local.site_url

    # Read automatically by AddApplicationInsightsTelemetry().
    "APPLICATIONINSIGHTS_CONNECTION_STRING" = azurerm_application_insights.this.connection_string
  }

  tags = local.tags

  lifecycle {
    # Deployments (zip deploy, VS publish) set this themselves; don't let the
    # next `apply` undo a deployment.
    ignore_changes = [app_settings["WEBSITE_RUN_FROM_PACKAGE"]]
  }
}
