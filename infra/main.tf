# =============================================================================
# Main: Medical Appointment No-Show Predictor Infrastructure
# =============================================================================
# CAF naming convention: {prefix}-{project}-{environment}-{region}-{instance}
# Uses AzApi provider per project specification
# =============================================================================

# -----------------------------------------------------------------------------
# Resource Group
# -----------------------------------------------------------------------------

resource "azapi_resource" "resource_group" {
  type     = "Microsoft.Resources/resourceGroups@2024-03-01"
  name     = "rg-${local.name_prefix}-001"
  location = var.location

  body = {
    tags = local.common_tags
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
# Module: Azure Container Registry
# -----------------------------------------------------------------------------

module "acr" {
  source = "./modules/acr"

  resource_group_name = azapi_resource.resource_group.name
  resource_group_id   = azapi_resource.resource_group.id
  location            = var.location
  name_prefix         = local.storage_name_prefix
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

  resource_group_name             = azapi_resource.resource_group.name
  resource_group_id               = azapi_resource.resource_group.id
  location                        = var.location
  name_prefix                     = local.name_prefix
  storage_name_prefix             = local.storage_name_prefix
  application_insights_id         = azapi_resource.app_insights.id
  container_registry_id           = module.acr.container_registry_id
  container_registry_login_server = module.acr.login_server
  tags                            = local.common_tags

  # Dependencies for RBAC
  sql_server_id           = module.sql.sql_server_id
  ml_workspace_id         = module.ml.workspace_id
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
