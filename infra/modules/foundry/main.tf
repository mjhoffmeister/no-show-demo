# =============================================================================
# Module: Azure AI Foundry
# =============================================================================
# Creates AI Foundry account, project, and GPT-4o deployment
# CAF prefix: aif (account), proj (project)
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

variable "storage_name_prefix" {
  type = string
}

variable "application_insights_id" {
  type = string
}

variable "container_registry_id" {
  type = string
}

variable "container_registry_login_server" {
  type        = string
  description = "ACR login server URL for the connection"
}

variable "tags" {
  type = map(string)
}

variable "sql_server_id" {
  type = string
}

variable "ml_workspace_id" {
  type = string
}

# -----------------------------------------------------------------------------
# Data Source for Current Client Config
# -----------------------------------------------------------------------------

data "azapi_client_config" "current" {}

# -----------------------------------------------------------------------------
# Storage Account for AI Foundry
# -----------------------------------------------------------------------------

resource "azapi_resource" "storage_foundry" {
  type      = "Microsoft.Storage/storageAccounts@2023-05-01"
  name      = "st${var.storage_name_prefix}aif001"
  location  = var.location
  parent_id = var.resource_group_id

  body = {
    kind = "StorageV2"
    sku = {
      name = "Standard_LRS"
    }
    properties = {
      minimumTlsVersion             = "TLS1_2"
      allowSharedKeyAccess          = false  # Entra ID auth only per constitution
      defaultToOAuthAuthentication  = true
      supportsHttpsTrafficOnly      = true
      publicNetworkAccess           = "Enabled"  # Required for local dev access
      accessTier                    = "Hot"
      encryption = {
        services = {
          blob = {
            enabled = true
          }
        }
        keySource = "Microsoft.Storage"
      }
    }
    tags = var.tags
  }

  response_export_values = ["id"]
}

# Blob services configuration (update existing auto-created default)
resource "azapi_update_resource" "storage_foundry_blob_services" {
  type        = "Microsoft.Storage/storageAccounts/blobServices@2023-05-01"
  resource_id = "${azapi_resource.storage_foundry.id}/blobServices/default"

  body = {
    properties = {
      deleteRetentionPolicy = {
        enabled = true
        days    = 7
      }
    }
  }
}

# -----------------------------------------------------------------------------
# Key Vault for AI Foundry
# -----------------------------------------------------------------------------

resource "azapi_resource" "keyvault_foundry" {
  type      = "Microsoft.KeyVault/vaults@2023-07-01"
  name      = "kv-${var.name_prefix}-aif"
  location  = var.location
  parent_id = var.resource_group_id

  body = {
    properties = {
      tenantId                  = data.azapi_client_config.current.tenant_id
      sku = {
        family = "A"
        name   = "standard"
      }
      enablePurgeProtection     = true  # Cannot disable once enabled
      softDeleteRetentionInDays = 90
      enableRbacAuthorization   = true
    }
    tags = var.tags
  }

  response_export_values = ["id"]

  # Workaround: Key Vault may exist from soft-delete recovery + AzApi provider bugs
  lifecycle {
    ignore_changes = all
  }
}

# -----------------------------------------------------------------------------
# AI Foundry Account (Cognitive Services)
# -----------------------------------------------------------------------------

resource "azapi_resource" "foundry_account" {
  type      = "Microsoft.CognitiveServices/accounts@2025-06-01"  # Use same API version as Bicep template
  name      = "aif-${var.name_prefix}-001"
  location  = var.location
  parent_id = var.resource_group_id

  schema_validation_enabled = false  # allowProjectManagement not in schema yet

  identity {
    type = "SystemAssigned"
  }

  body = {
    kind = "AIServices"
    sku = {
      name = "S0"
    }
    properties = {
      customSubDomainName    = "aif-${var.name_prefix}-001"
      publicNetworkAccess    = "Enabled"
      disableLocalAuth       = true
      allowProjectManagement = true
      networkAcls = {
        defaultAction       = "Allow"
        virtualNetworkRules = []
        ipRules             = []
      }
    }
    tags = var.tags
  }

  response_export_values = ["properties.endpoint", "properties.endpoints"]
}

# -----------------------------------------------------------------------------
# AI Foundry Project
# -----------------------------------------------------------------------------

resource "azapi_resource" "foundry_project" {
  type      = "Microsoft.CognitiveServices/accounts/projects@2025-04-01-preview"
  name      = "proj-${var.name_prefix}-001"
  location  = var.location
  parent_id = azapi_resource.foundry_account.id

  identity {
    type = "SystemAssigned"
  }

  body = {
    properties = {
      displayName = "No-Show Predictor"
      description = "Medical appointment no-show prediction agent"
    }
    tags = var.tags
  }

  response_export_values = ["properties", "identity"]
}

# -----------------------------------------------------------------------------
# Managed Identity for Agent
# -----------------------------------------------------------------------------

resource "azapi_resource" "agent_identity" {
  type      = "Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31"
  name      = "id-${var.name_prefix}-agent-001"
  location  = var.location
  parent_id = var.resource_group_id

  body = {
    tags = var.tags
  }

  response_export_values = ["properties.principalId", "properties.clientId"]
}

# -----------------------------------------------------------------------------
# Capability Host for Hosted Agents (Public Hosting - Foundry manages infra)
# -----------------------------------------------------------------------------
# With enablePublicHostingEnvironment=true, Foundry manages ACR/storage
# CRITICAL: Must depend on ACR connection being established first!

resource "azapi_resource" "capability_host" {
  type      = "Microsoft.CognitiveServices/accounts/capabilityHosts@2025-10-01-preview"
  name      = "agents"  # Must match azd ai agent expectations
  parent_id = azapi_resource.foundry_account.id

  body = {
    properties = {
      capabilityHostKind             = "Agents"
      enablePublicHostingEnvironment = true  # Key: Foundry manages infrastructure
    }
  }

  # Key learning from Bicep: capability host needs ACR connection to exist first
  depends_on = [
    azapi_resource.foundry_project,
    azapi_resource.acr_connection,
    azapi_resource.role_project_acr_pull
  ]

  timeouts {
    create = "10m"  # Capability host can take 6+ minutes to provision
  }
}

# -----------------------------------------------------------------------------
# ACR Connection to Foundry Project (for azd ai agent image push)
# -----------------------------------------------------------------------------
# This connection allows azd ai agent to push container images to ACR
# and for Foundry to pull them for hosted agent deployment

resource "azapi_resource" "acr_connection" {
  type      = "Microsoft.CognitiveServices/accounts/projects/connections@2025-04-01-preview"
  name      = "acr-connection"
  parent_id = azapi_resource.foundry_project.id

  body = {
    properties = {
      category      = "ContainerRegistry"
      target        = var.container_registry_login_server
      authType      = "ManagedIdentity"
      isSharedToAll = true
      credentials = {
        clientId   = azapi_resource.foundry_project.output.identity.principalId
        resourceId = var.container_registry_id
      }
      metadata = {
        ResourceId = var.container_registry_id
      }
    }
  }

  # Must wait for project AND project's ACR pull permission
  depends_on = [
    azapi_resource.foundry_project,
    azapi_resource.role_project_acr_pull
  ]

  # Workaround: AzApi provider has "Missing Resource Identity After Update" bug
  lifecycle {
    ignore_changes = [body]
  }
}

# -----------------------------------------------------------------------------
# Outputs
# -----------------------------------------------------------------------------

output "account_name" {
  value = azapi_resource.foundry_account.name
}

output "account_id" {
  value = azapi_resource.foundry_account.id
}

output "project_name" {
  value = azapi_resource.foundry_project.name
}

output "project_id" {
  value = azapi_resource.foundry_project.id
}

output "project_resource_id" {
  description = "Full ARM resource ID for azd ai agent init"
  value       = azapi_resource.foundry_project.id
}

output "endpoint" {
  value = azapi_resource.foundry_account.output.properties.endpoint
}

output "agent_managed_identity_id" {
  value = azapi_resource.agent_identity.id
}

output "agent_managed_identity_principal_id" {
  value = azapi_resource.agent_identity.output.properties.principalId
}

output "agent_managed_identity_client_id" {
  value = azapi_resource.agent_identity.output.properties.clientId
}
