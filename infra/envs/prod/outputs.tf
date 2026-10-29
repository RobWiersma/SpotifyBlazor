# Re-export the module's outputs so `terraform output` works from this folder.
output "resource_group_name" {
  value = module.app.resource_group_name
}

output "web_app_name" {
  value = module.app.web_app_name
}

output "site_url" {
  value = module.app.site_url
}

output "callback_uri" {
  value = module.app.callback_uri
}

output "key_vault_name" {
  value = module.app.key_vault_name
}
