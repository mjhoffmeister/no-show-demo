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

  response_export_values = ["properties.scoringUri"]
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
