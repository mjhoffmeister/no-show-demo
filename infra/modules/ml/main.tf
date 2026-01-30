# =============================================================================
# Module: Azure Machine Learning Workspace
# =============================================================================
# Creates ML Workspace, compute cluster, and storage
# CAF prefix: mlw (workspace)
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

variable "tags" {
  type = map(string)
}

# -----------------------------------------------------------------------------
# Data Source for Tenant ID
# -----------------------------------------------------------------------------

data "azapi_client_config" "current" {}

# -----------------------------------------------------------------------------
# Storage Account for ML Workspace
# Note: allowSharedKeyAccess=false and systemDatastoresAuthMode="identity" on
# the workspace ensure all access uses Entra ID authentication, not keys.
# -----------------------------------------------------------------------------

resource "azapi_resource" "storage_ml" {
  type      = "Microsoft.Storage/storageAccounts@2023-05-01"
  name      = "st${var.storage_name_prefix}ml001"
  location  = var.location
  parent_id = var.resource_group_id

  body = {
    kind = "StorageV2"
    sku = {
      name = "Standard_LRS"
    }
    properties = {
      minimumTlsVersion             = "TLS1_2"
      allowSharedKeyAccess          = false  # Entra ID auth only - no keys
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

  response_export_values = ["properties", "id"]
}

# Blob services configuration (update existing auto-created default)
resource "azapi_update_resource" "storage_ml_blob_services" {
  type        = "Microsoft.Storage/storageAccounts/blobServices@2023-05-01"
  resource_id = "${azapi_resource.storage_ml.id}/blobServices/default"

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
# Key Vault for ML Workspace
# -----------------------------------------------------------------------------

resource "azapi_resource" "keyvault_ml" {
  type      = "Microsoft.KeyVault/vaults@2023-07-01"
  name      = "kv-${var.name_prefix}-ml"
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
      enableRbacAuthorization   = true  # RBAC only - no access policies
    }
    tags = var.tags
  }

  response_export_values = ["id"]
}

# -----------------------------------------------------------------------------
# Azure ML Workspace
# -----------------------------------------------------------------------------

resource "azapi_resource" "ml_workspace" {
  type      = "Microsoft.MachineLearningServices/workspaces@2024-04-01"
  name      = "mlw-${var.name_prefix}-001"
  location  = var.location
  parent_id = var.resource_group_id

  identity {
    type = "SystemAssigned"
  }

  body = {
    properties = {
      applicationInsights      = var.application_insights_id
      keyVault                 = azapi_resource.keyvault_ml.id
      storageAccount           = azapi_resource.storage_ml.id
      containerRegistry        = var.container_registry_id
      publicNetworkAccess      = "Enabled"
      friendlyName             = "mlw-${var.name_prefix}-001"
      systemDatastoresAuthMode = "identity"  # Use managed identity, not keys
      v1LegacyMode             = false       # Use RBAC for Key Vault, not access policies
    }
    tags = var.tags
  }

  # Required: Azure normalizes casing differently than Terraform config
  ignore_casing             = true
  schema_validation_enabled = false  # systemDatastoresAuthMode not in local schema
  response_export_values    = ["id", "name", "identity"]  # Keep specific to avoid role replacement
}



# -----------------------------------------------------------------------------
# Compute Cluster for AutoML Training
# -----------------------------------------------------------------------------

resource "azapi_resource" "compute_cluster" {
  type      = "Microsoft.MachineLearningServices/workspaces/computes@2024-04-01"
  name      = "cpu-cluster"
  location  = var.location
  parent_id = azapi_resource.ml_workspace.id

  body = {
    properties = {
      computeType = "AmlCompute"
      properties = {
        vmSize         = "Standard_DS3_v2"
        vmPriority     = "LowPriority"
        scaleSettings = {
          minNodeCount                = 0
          maxNodeCount                = 2
          nodeIdleTimeBeforeScaleDown = "PT5M"
        }
      }
    }
    identity = {
      type = "SystemAssigned"
    }
    tags = var.tags
  }

  response_export_values = ["identity"]
}

# -----------------------------------------------------------------------------
# RBAC: Grant compute cluster Storage Blob Data Contributor on ML storage
# Required for identity-based access to training data
# -----------------------------------------------------------------------------

locals {
  role_definition_prefix        = "/subscriptions/${data.azapi_client_config.current.subscription_id}/providers/Microsoft.Authorization/roleDefinitions"
  role_storage_blob_contributor = "ba92f5b4-2d11-453d-a403-e96b0029c9fe"
}

# Note: Azure ML automatically grants the workspace identity Key Vault Administrator
# and Storage Blob Data Contributor roles when systemDatastoresAuthMode = "identity".
# We don't need to create these explicitly - Azure ML manages them.

resource "azapi_resource" "role_compute_storage_blob" {
  type      = "Microsoft.Authorization/roleAssignments@2022-04-01"
  name      = uuidv5("url", "${azapi_resource.storage_ml.id}-${azapi_resource.compute_cluster.output.identity.principalId}-storage")
  parent_id = azapi_resource.storage_ml.id

  body = {
    properties = {
      roleDefinitionId = "${local.role_definition_prefix}/${local.role_storage_blob_contributor}"
      principalId      = azapi_resource.compute_cluster.output.identity.principalId
      principalType    = "ServicePrincipal"
    }
  }
}

# Note: The workspaceblobstore datastore automatically uses managed identity
# authentication because the storage account has allowSharedKeyAccess = false.
# No explicit datastore reconfiguration is needed - Azure ML falls back to
# identity-based auth when shared key access is disabled.



# -----------------------------------------------------------------------------
# Outputs
# -----------------------------------------------------------------------------

output "workspace_name" {
  value = azapi_resource.ml_workspace.name
}

output "workspace_id" {
  value = azapi_resource.ml_workspace.id
}

output "storage_account_id" {
  value = azapi_resource.storage_ml.id
}

output "compute_cluster_name" {
  value = azapi_resource.compute_cluster.name
}

output "compute_cluster_identity_principal_id" {
  value = azapi_resource.compute_cluster.output.identity.principalId
}
