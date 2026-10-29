# `terraform test` runs plans against MOCK providers: no Azure login, no cost,
# fast enough for every PR. It checks the module's own logic (names,
# computed settings), not whether Azure would accept the request.
mock_provider "azurerm" {
  mock_data "azurerm_client_config" {
    defaults = {
      tenant_id = "00000000-0000-0000-0000-000000000000"
      object_id = "11111111-1111-1111-1111-111111111111"
    }
  }
}

mock_provider "random" {}

# Random values are unknown until apply; pin the suffix so names can be asserted.
override_resource {
  target          = random_string.suffix
  override_during = plan
  values = {
    result = "ab12"
  }
}

variables {
  spotify_client_id = "test-client-id"
}

run "dev_free_tier" {
  command = plan

  variables {
    environment     = "dev"
    app_service_sku = "F1"
  }

  assert {
    condition     = azurerm_linux_web_app.this.site_config[0].always_on == false
    error_message = "Free tier can't run always-on."
  }

  assert {
    condition     = azurerm_key_vault.this.purge_protection_enabled == false
    error_message = "Dev vaults must stay purgeable."
  }

  assert {
    condition     = startswith(azurerm_linux_web_app.this.app_settings["ConnectionStrings__clientSecret"], "@Microsoft.KeyVault(")
    error_message = "The Spotify client secret must be a Key Vault reference, never a literal."
  }
}

run "prod_defaults" {
  command = plan

  variables {
    environment = "prod"
  }

  assert {
    condition     = azurerm_linux_web_app.this.site_config[0].always_on
    error_message = "Paid tiers should keep the app loaded."
  }

  assert {
    condition     = azurerm_key_vault.this.purge_protection_enabled
    error_message = "Prod vault needs purge protection."
  }

  assert {
    condition     = length(azurerm_key_vault.this.name) <= 24
    error_message = "Key Vault names are limited to 24 characters."
  }

  assert {
    condition     = output.callback_uri == "https://app-spotifyblazor-prod-ab12.azurewebsites.net/spotcallback"
    error_message = "Default callback URI should point at the web app."
  }
}

run "custom_callback_uri" {
  command = plan

  variables {
    environment  = "prod"
    callback_uri = "https://music.example.com/spotcallback"
  }

  assert {
    condition     = azurerm_linux_web_app.this.app_settings["ConnectionStrings__callbackUri"] == "https://music.example.com/spotcallback"
    error_message = "callback_uri should override the default."
  }
}

run "rejects_unknown_environment" {
  command = plan

  variables {
    environment = "staging"
  }

  expect_failures = [var.environment]
}
