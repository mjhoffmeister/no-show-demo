# =============================================================================
# Module: Azure SQL Database
# =============================================================================
# Creates SQL Server and Database for appointment data
# CAF prefix: sql (server), sqldb (database)
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

variable "tags" {
  type = map(string)
}

# -----------------------------------------------------------------------------
# Data Source for Current Client Config
# -----------------------------------------------------------------------------

data "azapi_client_config" "current" {}

# -----------------------------------------------------------------------------
# Azure SQL Server
# -----------------------------------------------------------------------------

resource "azapi_resource" "sql_server" {
  type      = "Microsoft.Sql/servers@2023-08-01-preview"
  name      = "sql-${var.name_prefix}-001"
  location  = var.location
  parent_id = var.resource_group_id

  body = {
    properties = {
      version                       = "12.0"
      minimalTlsVersion             = "1.2"
      publicNetworkAccess           = "Enabled"
      administrators = {
        administratorType         = "ActiveDirectory"
        azureADOnlyAuthentication = true
        login                     = "sqladmin"
        principalType             = "User"
        sid                       = data.azapi_client_config.current.object_id
        tenantId                  = data.azapi_client_config.current.tenant_id
      }
    }
    tags = var.tags
  }

  response_export_values = ["id", "properties.fullyQualifiedDomainName"]
}

# -----------------------------------------------------------------------------
# Azure SQL Database (Basic tier for demo)
# -----------------------------------------------------------------------------

resource "azapi_resource" "sql_database" {
  type      = "Microsoft.Sql/servers/databases@2023-08-01-preview"
  name      = "sqldb-noshow"
  location  = var.location
  parent_id = azapi_resource.sql_server.id

  body = {
    sku = {
      name = "Basic"
      tier = "Basic"
    }
    properties = {
      collation                   = "SQL_Latin1_General_CP1_CI_AS"
      maxSizeBytes                = 2147483648  # 2GB
      zoneRedundant               = false
      requestedBackupStorageRedundancy = "Local"
    }
    tags = var.tags
  }

  response_export_values = ["id"]
}

# -----------------------------------------------------------------------------
# Firewall Rules
# -----------------------------------------------------------------------------

# Allow Azure services
resource "azapi_resource" "sql_firewall_azure" {
  type      = "Microsoft.Sql/servers/firewallRules@2023-08-01-preview"
  name      = "AllowAzureServices"
  parent_id = azapi_resource.sql_server.id

  body = {
    properties = {
      startIpAddress = "0.0.0.0"
      endIpAddress   = "0.0.0.0"
    }
  }
}

# -----------------------------------------------------------------------------
# Outputs
# -----------------------------------------------------------------------------

output "sql_server_id" {
  value = azapi_resource.sql_server.id
}

output "server_name" {
  value = azapi_resource.sql_server.name
}

output "server_fqdn" {
  value = azapi_resource.sql_server.output.properties.fullyQualifiedDomainName
}

output "database_name" {
  value = azapi_resource.sql_database.name
}

output "database_id" {
  value = azapi_resource.sql_database.id
}

output "connection_string" {
  value     = "Server=tcp:${azapi_resource.sql_server.output.properties.fullyQualifiedDomainName},1433;Database=${azapi_resource.sql_database.name};Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False;"
  sensitive = true
}
