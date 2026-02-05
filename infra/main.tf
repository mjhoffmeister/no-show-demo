# =============================================================================
# Main: Medical Appointment No-Show Predictor Infrastructure
# =============================================================================
# CAF naming convention: {prefix}-{project}-{environment}-{region}-{instance}
# Uses AzApi provider per project specification
# =============================================================================

# NOTE: If deploying fresh and kv-*-aif exists in soft-delete state, uncomment:
# import {
#   to = module.foundry.azapi_resource.keyvault_foundry
#   id = "/subscriptions/${var.subscription_id}/resourceGroups/rg-${local.name_prefix}-001/providers/Microsoft.KeyVault/vaults/kv-${local.name_prefix}-aif"
# }

# -----------------------------------------------------------------------------
# Resource Group
# -----------------------------------------------------------------------------

resource "azapi_resource" "resource_group" {
  type     = "Microsoft.Resources/resourceGroups@2024-03-01"
  name     = "rg-${local.name_prefix}-001"
  location = var.location

  body = {}

  # Don't update tags - AzApi provider has a bug with resource group tag updates
  lifecycle {
    ignore_changes = [body]
  }
}

# -----------------------------------------------------------------------------
# Log Analytics Workspace (shared by all services)
# -----------------------------------------------------------------------------

resource "azapi_resource" "log_analytics" {
  type      = "Microsoft.OperationalInsights/workspaces@2023-09-01"
  name      = "log-${local.name_prefix}-001"
  location  = var.location
  parent_id = azapi_resource.resource_group.id

  body = {
    properties = {
      sku = {
        name = "PerGB2018"
      }
      retentionInDays = 30
    }
    tags = local.common_tags
  }

  response_export_values = ["id"]
}

# -----------------------------------------------------------------------------
# Application Insights (shared telemetry)
# -----------------------------------------------------------------------------

resource "azapi_resource" "app_insights" {
  type      = "Microsoft.Insights/components@2020-02-02"
  name      = "appi-${local.name_prefix}-001"
  location  = var.location
  parent_id = azapi_resource.resource_group.id

  body = {
    kind = "web"
    properties = {
      Application_Type                = "web"
      WorkspaceResourceId             = azapi_resource.log_analytics.id
      publicNetworkAccessForIngestion = "Enabled"
      publicNetworkAccessForQuery     = "Enabled"
    }
    tags = local.common_tags
  }

  response_export_values = ["id", "properties.ConnectionString", "properties.InstrumentationKey"]
}

# -----------------------------------------------------------------------------
# Entra ID: SPA App Registration for Browser Auth
# -----------------------------------------------------------------------------

data "azuread_client_config" "current" {}

# Microsoft's well-known Azure AI service principal (same across all tenants)
# Used to request user_impersonation scope for AI Foundry access
data "azuread_service_principal" "azure_ai" {
  client_id = "18a66f5f-dbdf-4c17-9dd7-1634712a9cbe"
}

# -----------------------------------------------------------------------------
# Module: Azure Container Registry
# -----------------------------------------------------------------------------

module "acr" {
  source = "./modules/acr"

  resource_group_name = azapi_resource.resource_group.name
  resource_group_id   = azapi_resource.resource_group.id
  location            = var.location
  name_prefix         = local.name_prefix
  tags                = local.common_tags
}

# -----------------------------------------------------------------------------
# Module: Azure SQL Database
# -----------------------------------------------------------------------------

module "sql" {
  source = "./modules/sql"

  resource_group_name = azapi_resource.resource_group.name
  resource_group_id   = azapi_resource.resource_group.id
  location            = var.location
  name_prefix         = local.name_prefix
  tags                = local.common_tags
}

# -----------------------------------------------------------------------------
# Module: Azure ML Workspace
# -----------------------------------------------------------------------------

module "ml" {
  source = "./modules/ml"

  resource_group_name        = azapi_resource.resource_group.name
  resource_group_id          = azapi_resource.resource_group.id
  location                   = var.location
  name_prefix                = local.name_prefix
  storage_name_prefix        = local.storage_name_prefix
  application_insights_id    = azapi_resource.app_insights.id
  container_registry_id      = module.acr.container_registry_id
  tags                       = local.common_tags
}

# -----------------------------------------------------------------------------
# Module: Azure AI Foundry
# -----------------------------------------------------------------------------

module "foundry" {
  source = "./modules/foundry"

  resource_group_name                    = azapi_resource.resource_group.name
  resource_group_id                      = azapi_resource.resource_group.id
  location                               = var.location
  name_prefix                            = local.name_prefix
  storage_name_prefix                    = local.storage_name_prefix
  application_insights_id                = azapi_resource.app_insights.id
  application_insights_connection_string = azapi_resource.app_insights.output.properties.ConnectionString
  container_registry_id                  = module.acr.container_registry_id
  container_registry_login_server        = module.acr.login_server
  sql_server_id                          = module.sql.sql_server_id
  ml_workspace_id                        = module.ml.workspace_id
  tags                                   = local.common_tags
}

# -----------------------------------------------------------------------------
# Module: Azure Static Web Apps
# -----------------------------------------------------------------------------

module "static_web_app" {
  source = "./modules/static-web-app"

  resource_group_name = azapi_resource.resource_group.name
  resource_group_id   = azapi_resource.resource_group.id
  location            = var.location
  name_prefix         = local.name_prefix
  tags                = local.common_tags
}

locals {
  spa_host = replace(module.static_web_app.default_host_name, "https://", "")
  spa_redirect_uris = [
    "https://${local.spa_host}/authentication/login-callback",
    "https://${local.spa_host}/authentication/logout-callback",
    "http://localhost:5000/authentication/login-callback",
    "http://localhost:5000/authentication/logout-callback"
  ]
}

resource "azuread_application" "spa" {
  display_name     = "noshow-predictor-spa-${var.environment}"
  sign_in_audience = "AzureADMyOrg"
  owners           = [data.azuread_client_config.current.object_id]

  single_page_application {
    redirect_uris = local.spa_redirect_uris
  }

  required_resource_access {
    resource_app_id = data.azuread_service_principal.azure_ai.application_id

    resource_access {
      id   = data.azuread_service_principal.azure_ai.oauth2_permission_scopes[0].id
      type = "Scope"
    }
  }
}

resource "azuread_service_principal" "spa" {
  client_id = azuread_application.spa.client_id
  owners    = [data.azuread_client_config.current.object_id]
}
