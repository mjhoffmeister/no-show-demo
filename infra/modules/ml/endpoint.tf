# =============================================================================
# Azure ML Managed Online Endpoint
# =============================================================================
# Creates the endpoint for real-time model inference
# Deployment is handled via Azure ML SDK after model training
# =============================================================================

# -----------------------------------------------------------------------------
# Managed Online Endpoint
# -----------------------------------------------------------------------------

resource "azapi_resource" "ml_endpoint" {
  type      = "Microsoft.MachineLearningServices/workspaces/onlineEndpoints@2024-10-01"
  name      = "noshow-predictor"
  location  = var.location
  parent_id = azapi_resource.ml_workspace.id

  identity {
    type = "SystemAssigned"
  }

  body = {
    properties = {
      authMode            = "AADToken"
      publicNetworkAccess = "Enabled"
      description         = "No-show prediction model endpoint"
    }
    tags = var.tags
  }

  response_export_values = ["properties.scoringUri", "identity"]
}

# -----------------------------------------------------------------------------
# RBAC: Grant endpoint Storage Blob Data Contributor on ML storage
# Required for endpoint to download model artifacts during deployment
# -----------------------------------------------------------------------------

resource "azapi_resource" "role_endpoint_storage_blob" {
  type      = "Microsoft.Authorization/roleAssignments@2022-04-01"
  name      = uuidv5("url", "${azapi_resource.storage_ml.id}-${azapi_resource.ml_endpoint.output.identity.principalId}-endpoint-storage")
  parent_id = azapi_resource.storage_ml.id

  body = {
    properties = {
      roleDefinitionId = "${local.role_definition_prefix}/${local.role_storage_blob_contributor}"
      principalId      = azapi_resource.ml_endpoint.output.identity.principalId
      principalType    = "ServicePrincipal"
    }
  }
}

# -----------------------------------------------------------------------------
# RBAC: Grant endpoint AzureML Data Scientist on workspace
# Required for endpoint to access registered models and artifacts
# -----------------------------------------------------------------------------

locals {
  role_azureml_data_scientist = "f6c7c914-8db3-469d-8ca1-694a8f32e121"
}

resource "azapi_resource" "role_endpoint_workspace" {
  type      = "Microsoft.Authorization/roleAssignments@2022-04-01"
  name      = uuidv5("url", "${azapi_resource.ml_workspace.id}-${azapi_resource.ml_endpoint.output.identity.principalId}-endpoint-workspace")
  parent_id = azapi_resource.ml_workspace.id

  body = {
    properties = {
      roleDefinitionId = "${local.role_definition_prefix}/${local.role_azureml_data_scientist}"
      principalId      = azapi_resource.ml_endpoint.output.identity.principalId
      principalType    = "ServicePrincipal"
    }
  }
}

# -----------------------------------------------------------------------------
# Outputs
# -----------------------------------------------------------------------------

output "endpoint_name" {
  value = azapi_resource.ml_endpoint.name
}

output "endpoint_url" {
  value = try(azapi_resource.ml_endpoint.output.properties.scoringUri, "https://${azapi_resource.ml_endpoint.name}.${var.location}.inference.ml.azure.com/score")
}

output "endpoint_id" {
  value = azapi_resource.ml_endpoint.id
}
