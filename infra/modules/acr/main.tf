# =============================================================================
# Module: Azure Container Registry
# =============================================================================
# Creates ACR for agent container images
# CAF prefix: cr (no hyphens allowed)
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
# Azure Container Registry
# -----------------------------------------------------------------------------

resource "azapi_resource" "acr" {
  # ACR names must be alphanumeric only (no hyphens)
  type      = "Microsoft.ContainerRegistry/registries@2023-07-01"
  name      = "cr${replace(var.name_prefix, "-", "")}001"
  location  = var.location
  parent_id = var.resource_group_id

  body = {
    sku = {
      name = "Basic"
    }
    properties = {
      adminUserEnabled = false
    }
    tags = var.tags
  }

  response_export_values = ["id", "properties.loginServer"]
}

# -----------------------------------------------------------------------------
# Outputs
# -----------------------------------------------------------------------------

output "name" {
  value = azapi_resource.acr.name
}

output "container_registry_id" {
  value = azapi_resource.acr.id
}

output "login_server" {
  value = azapi_resource.acr.output.properties.loginServer
}
