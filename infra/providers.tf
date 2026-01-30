terraform {
  required_version = ">= 1.5.0"

  required_providers {
    azapi = {
      source  = "Azure/azapi"
      version = "2.8.0"
    }
  }
}

provider "azapi" {
  # Uses DefaultAzureCredential - no explicit credentials
  # Configuration inherited from environment or Azure CLI login
}
