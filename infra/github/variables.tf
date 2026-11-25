variable "github_repository" {
  description = "owner/name of the GitHub repository whose workflows are trusted"
  type        = string
  default     = "RobWiersma/SpotifyBlazor"
}

variable "live_web_app_name" {
  description = "The App Service the deploy workflow publishes to"
  type        = string
  default     = "SpotifyBlazor"
}

variable "live_resource_group_name" {
  type    = string
  default = "ResourceGroup1"
}

variable "tfstate_storage_account" {
  description = "Storage account made by infra/bootstrap (terraform output storage_account_name there)"
  type        = string
}
