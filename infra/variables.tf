# =============================================================================
# Variables: Medical Appointment No-Show Predictor
# =============================================================================

variable "subscription_id" {
  description = "Azure subscription ID"
  type        = string
}

variable "location" {
  description = "Azure region for resources (North Central US required for hosted agents)"
  type        = string
  default     = "northcentralus"

  validation {
    condition     = var.location == "northcentralus"
    error_message = "Location must be 'northcentralus' for Azure AI Foundry hosted agents preview."
  }
}

variable "resource_prefix" {
  description = "Prefix for resource names (e.g., 'noshow')"
  type        = string
  default     = "noshow"

  validation {
    condition     = can(regex("^[a-z][a-z0-9]{2,10}$", var.resource_prefix))
    error_message = "Resource prefix must be 3-11 lowercase alphanumeric characters starting with a letter."
  }
}

variable "environment" {
  description = "Environment name (dev, test, prod)"
  type        = string
  default     = "dev"

  validation {
    condition     = contains(["dev", "test", "prod"], var.environment)
    error_message = "Environment must be one of: dev, test, prod."
  }
}

variable "tags" {
  description = "Tags to apply to all resources"
  type        = map(string)
  default     = {}
}

# -----------------------------------------------------------------------------
# Computed Local Values
# -----------------------------------------------------------------------------

locals {
  # CAF naming convention: {prefix}-{project}-{environment}-{region}-{instance}
  # Shortened region codes for naming
  region_short = {
    "northcentralus" = "ncus"
    "eastus"         = "eus"
    "eastus2"        = "eus2"
    "westus"         = "wus"
    "westus2"        = "wus2"
  }

  region_code = lookup(local.region_short, var.location, "ncus")

  # Standard name prefix for most resources
  name_prefix = "${var.resource_prefix}-${var.environment}-${local.region_code}"

  # Storage accounts and container registries have special naming rules (no hyphens, max 24 chars)
  storage_name_prefix = "${var.resource_prefix}${var.environment}${local.region_code}"

  # Common tags applied to all resources
  common_tags = merge(var.tags, {
    Environment = var.environment
    Project     = "no-show-predictor"
    ManagedBy   = "terraform"
  })
}
