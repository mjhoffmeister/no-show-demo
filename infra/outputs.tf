# =============================================================================
# Outputs: Medical Appointment No-Show Predictor
# =============================================================================
# Exposes all infrastructure endpoints for azd and services
# =============================================================================

# -----------------------------------------------------------------------------
# Resource Group
# -----------------------------------------------------------------------------

output "resource_group_name" {
  description = "Name of the resource group"
  value       = azapi_resource.resource_group.name
}

output "resource_group_id" {
  description = "ID of the resource group"
  value       = azapi_resource.resource_group.id
}

# -----------------------------------------------------------------------------
# Azure Container Registry
# -----------------------------------------------------------------------------

output "acr_name" {
  description = "Azure Container Registry name"
  value       = module.acr.name
}

output "acr_login_server" {
  description = "Azure Container Registry login server"
  value       = module.acr.login_server
}

# -----------------------------------------------------------------------------
# Azure AI Foundry
# -----------------------------------------------------------------------------

output "foundry_account_name" {
  description = "Azure AI Foundry account name"
  value       = module.foundry.account_name
}

output "foundry_project_name" {
  description = "Azure AI Foundry project name"
  value       = module.foundry.project_name
}

output "foundry_project_id" {
  description = "Azure AI Foundry project resource ID"
  value       = module.foundry.project_id
}

output "foundry_endpoint" {
  description = "Azure AI Foundry endpoint"
  value       = module.foundry.endpoint
}

output "foundry_project_identity_principal_id" {
  description = "Project identity principal ID (used by hosted agents)"
  value       = module.foundry.project_identity_principal_id
}

# -----------------------------------------------------------------------------
# Azure SQL Database
# -----------------------------------------------------------------------------

output "sql_server_fqdn" {
  description = "Azure SQL Server fully qualified domain name"
  value       = module.sql.server_fqdn
}

output "sql_database_name" {
  description = "Azure SQL Database name"
  value       = module.sql.database_name
}

output "sql_connection_string" {
  description = "Azure SQL Database connection string (Managed Identity)"
  value       = module.sql.connection_string
  sensitive   = true
}

# -----------------------------------------------------------------------------
# Azure ML Workspace
# -----------------------------------------------------------------------------

output "ml_workspace_name" {
  description = "Azure ML Workspace name"
  value       = module.ml.workspace_name
}

output "ml_workspace_id" {
  description = "Azure ML Workspace resource ID"
  value       = module.ml.workspace_id
}

output "ml_endpoint_url" {
  description = "Azure ML managed online endpoint URL"
  value       = module.ml.endpoint_url
}

# -----------------------------------------------------------------------------
# Azure Static Web Apps
# -----------------------------------------------------------------------------

output "static_web_app_url" {
  description = "Azure Static Web App URL"
  value       = module.static_web_app.default_host_name
}

output "static_web_app_api_key" {
  description = "Azure Static Web App deployment API key"
  value       = module.static_web_app.api_key
  sensitive   = true
}

# -----------------------------------------------------------------------------
# Application Insights
# -----------------------------------------------------------------------------

output "application_insights_id" {
  description = "Application Insights resource ID"
  value       = azapi_resource.app_insights.id
}

output "application_insights_connection_string" {
  description = "Application Insights connection string"
  value       = azapi_resource.app_insights.output.properties.ConnectionString
  sensitive   = true
}

output "application_insights_instrumentation_key" {
  description = "Application Insights instrumentation key"
  value       = azapi_resource.app_insights.output.properties.InstrumentationKey
  sensitive   = true
}

# -----------------------------------------------------------------------------
# Entra ID App Registration (SPA)
# -----------------------------------------------------------------------------

output "aad_app_client_id" {
  description = "Entra ID app registration (SPA) client ID"
  value       = azuread_application.spa.client_id
}

output "aad_tenant_id" {
  description = "Azure tenant ID"
  value       = data.azuread_client_config.current.tenant_id
}
