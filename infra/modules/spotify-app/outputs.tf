output "resource_group_name" {
  value = azurerm_resource_group.this.name
}

output "web_app_name" {
  description = "Deploy target for `az webapp deploy` / Visual Studio publish."
  value       = azurerm_linux_web_app.this.name
}

output "site_url" {
  value = local.site_url
}

output "callback_uri" {
  description = "Add this to the Redirect URIs of the Spotify app."
  value       = local.callback_uri
}

output "key_vault_name" {
  description = "Vault to put spotify-client-secret and jwt-signing-key into."
  value       = azurerm_key_vault.this.name
}

output "application_insights_connection_string" {
  value     = azurerm_application_insights.this.connection_string
  sensitive = true
}
