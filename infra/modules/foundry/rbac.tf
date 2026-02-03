# =============================================================================
# RBAC Role Assignments for AI Foundry Agent
# =============================================================================
# Grants the agent managed identity access to required Azure services
# Following least-privilege principle per constitution
# Uses AzApi provider per project specification
# =============================================================================

# Built-in role definition IDs (using full subscription path to match Azure API response)
locals {
  role_definition_prefix          = "/subscriptions/${data.azapi_client_config.current.subscription_id}/providers/Microsoft.Authorization/roleDefinitions"
  role_cognitive_services_user    = "a97b65f3-24c7-4388-baec-2e87135dc908"
  role_azureml_data_scientist     = "f6c7c914-8db3-469d-8ca1-694a8f32e121"
  role_sql_db_contributor         = "9b7fa17d-e63e-47b0-bb0a-15c516ac86ec"
  role_keyvault_secrets_user      = "4633458b-17de-408a-b874-0445c86b69e6"
  role_storage_blob_contributor   = "ba92f5b4-2d11-453d-a403-e96b0029c9fe"
  role_acr_pull                   = "7f951dda-4ed3-4680-a7ca-43fe172d538d"  # AcrPull
}

# -----------------------------------------------------------------------------
# Cognitive Services User - Access to AI Foundry
# -----------------------------------------------------------------------------

resource "azapi_resource" "role_agent_cognitive_services" {
  type      = "Microsoft.Authorization/roleAssignments@2022-04-01"
  name      = uuidv5("url", "${azapi_resource.foundry_account.id}-${azapi_resource.agent_identity.output.properties.principalId}-cognitive")
  parent_id = azapi_resource.foundry_account.id

  body = {
    properties = {
      roleDefinitionId = "${local.role_definition_prefix}/${local.role_cognitive_services_user}"
      principalId      = azapi_resource.agent_identity.output.properties.principalId
      principalType    = "ServicePrincipal"
    }
  }
}

# -----------------------------------------------------------------------------
# Azure ML Data Scientist - Access to ML Workspace and Endpoints
# -----------------------------------------------------------------------------

resource "azapi_resource" "role_agent_ml_data_scientist" {
  type      = "Microsoft.Authorization/roleAssignments@2022-04-01"
  name      = uuidv5("url", "${var.ml_workspace_id}-${azapi_resource.agent_identity.output.properties.principalId}-mldata")
  parent_id = var.ml_workspace_id

  body = {
    properties = {
      roleDefinitionId = "${local.role_definition_prefix}/${local.role_azureml_data_scientist}"
      principalId      = azapi_resource.agent_identity.output.properties.principalId
      principalType    = "ServicePrincipal"
    }
  }
}

# -----------------------------------------------------------------------------
# SQL DB Contributor - Access to Azure SQL Database
# Note: Additional SQL-level permissions configured via T-SQL
# -----------------------------------------------------------------------------

resource "azapi_resource" "role_agent_sql_contributor" {
  type      = "Microsoft.Authorization/roleAssignments@2022-04-01"
  name      = uuidv5("url", "${var.sql_server_id}-${azapi_resource.agent_identity.output.properties.principalId}-sql")
  parent_id = var.sql_server_id

  body = {
    properties = {
      roleDefinitionId = "${local.role_definition_prefix}/${local.role_sql_db_contributor}"
      principalId      = azapi_resource.agent_identity.output.properties.principalId
      principalType    = "ServicePrincipal"
    }
  }
}

# -----------------------------------------------------------------------------
# Key Vault Secrets User - Access to secrets if needed
# -----------------------------------------------------------------------------

resource "azapi_resource" "role_agent_keyvault_secrets" {
  type      = "Microsoft.Authorization/roleAssignments@2022-04-01"
  name      = uuidv5("url", "${azapi_resource.keyvault_foundry.id}-${azapi_resource.agent_identity.output.properties.principalId}-kv")
  parent_id = azapi_resource.keyvault_foundry.id

  body = {
    properties = {
      roleDefinitionId = "${local.role_definition_prefix}/${local.role_keyvault_secrets_user}"
      principalId      = azapi_resource.agent_identity.output.properties.principalId
      principalType    = "ServicePrincipal"
    }
  }
}

# -----------------------------------------------------------------------------
# Storage Blob Data Contributor - Access to storage for ML artifacts
# -----------------------------------------------------------------------------

resource "azapi_resource" "role_agent_storage_blob" {
  type      = "Microsoft.Authorization/roleAssignments@2022-04-01"
  name      = uuidv5("url", "${azapi_resource.storage_foundry.id}-${azapi_resource.agent_identity.output.properties.principalId}-storage")
  parent_id = azapi_resource.storage_foundry.id

  body = {
    properties = {
      roleDefinitionId = "${local.role_definition_prefix}/${local.role_storage_blob_contributor}"
      principalId      = azapi_resource.agent_identity.output.properties.principalId
      principalType    = "ServicePrincipal"
    }
  }
}

# -----------------------------------------------------------------------------
# ACR Pull - Project identity needs to pull container images for hosted agents
# -----------------------------------------------------------------------------

resource "azapi_resource" "role_project_acr_pull" {
  type      = "Microsoft.Authorization/roleAssignments@2022-04-01"
  name      = uuidv5("url", "${var.container_registry_id}-${azapi_resource.foundry_project.output.identity.principalId}-acrpull")
  parent_id = var.container_registry_id

  body = {
    properties = {
      roleDefinitionId = "${local.role_definition_prefix}/${local.role_acr_pull}"
      principalId      = azapi_resource.foundry_project.output.identity.principalId
      principalType    = "ServicePrincipal"
    }
  }
}
