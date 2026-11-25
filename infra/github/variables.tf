variable "github_repository" {
  description = "owner/name of the GitHub repository whose workflows are trusted"
  type        = string
  default     = "RobWiersma/SpotifyBlazor"
}

# What GitHub puts before ":environment:…" or ":ref:…" in a workflow's OIDC
# token. This repository uses immutable subjects, which include the owner's
# and repository's numeric ids so a renamed or re-created repo can't match:
#   repo:<owner>@<owner id>/<repo>@<repo id>
# Check it with: gh api repos/<owner>/<repo>/actions/oidc/customization/sub
variable "github_subject_prefix" {
  description = "sub_claim_prefix of the repository's OIDC tokens"
  type        = string
  default     = "repo:RobWiersma@25392420/SpotifyBlazor@1323452149"
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
