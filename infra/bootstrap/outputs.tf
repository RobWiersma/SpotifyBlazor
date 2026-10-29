# Feed these into `terraform init -backend-config=...` for each environment
# (and into the TFSTATE_STORAGE_ACCOUNT variable in GitHub).
output "resource_group_name" {
  value = azurerm_resource_group.tfstate.name
}

output "storage_account_name" {
  value = azurerm_storage_account.tfstate.name
}

output "container_name" {
  value = azurerm_storage_container.tfstate.name
}
