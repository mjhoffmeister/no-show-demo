# =============================================================================
# GPT-4o Model Deployment for AI Foundry
# =============================================================================

# -----------------------------------------------------------------------------
# GPT-4o Deployment
# -----------------------------------------------------------------------------

resource "azapi_resource" "gpt4o_deployment" {
  type      = "Microsoft.CognitiveServices/accounts/deployments@2024-10-01"
  name      = "gpt-4o"
  parent_id = azapi_resource.foundry_account.id

  body = {
    sku = {
      name     = "Standard"
      capacity = 30  # 30K TPM for demo scale
    }
    properties = {
      model = {
        format  = "OpenAI"
        name    = "gpt-4o"
        version = "2024-11-20"
      }
      raiPolicyName = "Microsoft.DefaultV2"
    }
  }
}

# -----------------------------------------------------------------------------
# Output
# -----------------------------------------------------------------------------

output "gpt4o_deployment_name" {
  value = azapi_resource.gpt4o_deployment.name
}
