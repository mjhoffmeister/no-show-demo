# =============================================================================
# RBAC Role Assignments for AI Foundry Hosted Agent
# =============================================================================
# Hosted agents run with the Foundry PROJECT's system-assigned managed identity.
# Grants the project identity access to required Azure services.
# Following least-privilege principle per constitution.
# Uses AzApi provider per project specification.
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

# The project's system-assigned identity is what hosted agents use
locals {
  project_principal_id = azapi_resource.foundry_project.output.identity.principalId
}

# -----------------------------------------------------------------------------
# Cognitive Services User - Access to AI Foundry models
# -----------------------------------------------------------------------------

resource "azapi_resource" "role_project_cognitive_services" {
  type      = "Microsoft.Authorization/roleAssignments@2022-04-01"
  name      = uuidv5("url", "${azapi_resource.foundry_account.id}-${local.project_principal_id}-cognitive")
  parent_id = azapi_resource.foundry_account.id

  body = {
    properties = {
      roleDefinitionId = "${local.role_definition_prefix}/${local.role_cognitive_services_user}"
      principalId      = local.project_principal_id
      principalType    = "ServicePrincipal"
    }
  }
}

# -----------------------------------------------------------------------------
# Azure ML Data Scientist - Access to ML Workspace and Endpoints
# -----------------------------------------------------------------------------

resource "azapi_resource" "role_project_ml_data_scientist" {
  type      = "Microsoft.Authorization/roleAssignments@2022-04-01"
  name      = uuidv5("url", "${var.ml_workspace_id}-${local.project_principal_id}-mldata")
  parent_id = var.ml_workspace_id

  body = {
    properties = {
      roleDefinitionId = "${local.role_definition_prefix}/${local.role_azureml_data_scientist}"
      principalId      = local.project_principal_id
      principalType    = "ServicePrincipal"
    }
  }
}

# -----------------------------------------------------------------------------
# SQL DB Contributor - Access to Azure SQL Database
# Note: Also need T-SQL user creation for database-level permissions
# -----------------------------------------------------------------------------

resource "azapi_resource" "role_project_sql_contributor" {
  type      = "Microsoft.Authorization/roleAssignments@2022-04-01"
  name      = uuidv5("url", "${var.sql_server_id}-${local.project_principal_id}-sql")
  parent_id = var.sql_server_id

  body = {
    properties = {
      roleDefinitionId = "${local.role_definition_prefix}/${local.role_sql_db_contributor}"
      principalId      = local.project_principal_id
      principalType    = "ServicePrincipal"
    }
  }
}

# -----------------------------------------------------------------------------
# Key Vault Secrets User - Access to secrets if needed
# -----------------------------------------------------------------------------

resource "azapi_resource" "role_project_keyvault_secrets" {
  type      = "Microsoft.Authorization/roleAssignments@2022-04-01"
  name      = uuidv5("url", "${azapi_resource.keyvault_foundry.id}-${local.project_principal_id}-kv")
  parent_id = azapi_resource.keyvault_foundry.id

  body = {
    properties = {
      roleDefinitionId = "${local.role_definition_prefix}/${local.role_keyvault_secrets_user}"
      principalId      = local.project_principal_id
      principalType    = "ServicePrincipal"
    }
  }
}

# -----------------------------------------------------------------------------
# Storage Blob Data Contributor - Access to storage for ML artifacts
# -----------------------------------------------------------------------------

resource "azapi_resource" "role_project_storage_blob" {
  type      = "Microsoft.Authorization/roleAssignments@2022-04-01"
  name      = uuidv5("url", "${azapi_resource.storage_foundry.id}-${local.project_principal_id}-storage")
  parent_id = azapi_resource.storage_foundry.id

  body = {
    properties = {
      roleDefinitionId = "${local.role_definition_prefix}/${local.role_storage_blob_contributor}"
      principalId      = local.project_principal_id
      principalType    = "ServicePrincipal"
    }
  }
}

# -----------------------------------------------------------------------------
# ACR Pull - Project identity needs to pull container images for hosted agents
# -----------------------------------------------------------------------------

resource "azapi_resource" "role_project_acr_pull" {
  type      = "Microsoft.Authorization/roleAssignments@2022-04-01"
  name      = uuidv5("url", "${var.container_registry_id}-${local.project_principal_id}-acrpull")
  parent_id = var.container_registry_id

  body = {
    properties = {
      roleDefinitionId = "${local.role_definition_prefix}/${local.role_acr_pull}"
      principalId      = local.project_principal_id
      principalType    = "ServicePrincipal"
    }
  }
}
