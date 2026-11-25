# Copy these into GitHub → Settings → Secrets and variables → Actions →
# Variables. They're identifiers, not secrets: the trust is the federated
# credential, so knowing them doesn't let anyone log in.

output "AZURE_DEPLOY_CLIENT_ID" {
  value = azuread_application.deploy.client_id
}

output "AZURE_CLIENT_ID" {
  description = "The plan identity, used by terraform.yml"
  value       = azuread_application.plan.client_id
}

output "AZURE_TENANT_ID" {
  value = data.azurerm_client_config.current.tenant_id
}

output "AZURE_SUBSCRIPTION_ID" {
  value = data.azurerm_client_config.current.subscription_id
}

output "AZURE_WEBAPP_NAME" {
  value = data.azurerm_windows_web_app.live.name
}
