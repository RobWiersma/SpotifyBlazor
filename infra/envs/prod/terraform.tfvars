# Non-secret, per-environment values. Terraform loads terraform.tfvars
# automatically. Never put secrets in a .tfvars file: it's committed to git.

# Free tier: no cost, but no always-on (cold start after ~20 min idle), a
# 60 CPU-minute daily quota, and no custom domains. Change to "B1" to lift
# those limits; `terraform plan` will show it as an in-place update.
app_service_sku = "F1"
