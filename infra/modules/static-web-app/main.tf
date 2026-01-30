# =============================================================================
# Module: Azure Static Web Apps
# =============================================================================
# Creates Static Web App for Blazor WASM frontend
# CAF prefix: stapp
# Uses AzApi provider per project specification
# =============================================================================

variable "resource_group_name" {
  type = string
}

variable "resource_group_id" {
  type = string
}

variable "location" {
  type = string
}

variable "name_prefix" {
  type = string
}

variable "tags" {
  type = map(string)
}

# -----------------------------------------------------------------------------
# Azure Static Web App
# -----------------------------------------------------------------------------

resource "azapi_resource" "static_web_app" {
  type      = "Microsoft.Web/staticSites@2023-12-01"
  name      = "stapp-${var.name_prefix}-001"
  # Static Web Apps not available in northcentralus - use eastus2
  location  = "eastus2"
  parent_id = var.resource_group_id

  body = {
    sku = {
      name = "Free"
      tier = "Free"
    }
    properties = {}
    tags = var.tags
  }

  response_export_values = ["properties.defaultHostname"]
}

# Get API key via list secrets action (not returned in GET response)
resource "azapi_resource_action" "static_web_app_secrets" {
  type        = "Microsoft.Web/staticSites@2023-12-01"
  resource_id = azapi_resource.static_web_app.id
  action      = "listSecrets"
  method      = "POST"

  response_export_values = ["properties.apiKey"]
}

# -----------------------------------------------------------------------------
# Outputs
# -----------------------------------------------------------------------------

output "name" {
  value = azapi_resource.static_web_app.name
}

output "id" {
  value = azapi_resource.static_web_app.id
}

output "default_host_name" {
  value = "https://${azapi_resource.static_web_app.output.properties.defaultHostname}"
}

output "api_key" {
  value     = azapi_resource_action.static_web_app_secrets.output.properties.apiKey
  sensitive = true
}
