<#
.SYNOPSIS
    Grants SQL database access to the AI Foundry project's managed identity.

.DESCRIPTION
    Hosted agents in Azure AI Foundry run with the project's system-assigned managed identity.
    This script creates a contained database user for that identity and grants db_datareader.
    
    The RBAC role "SQL DB Contributor" only grants management-plane access.
    For data-plane access (SELECT, etc.), you need a database-level user.

.PARAMETER SqlServer
    The SQL server name (without .database.windows.net).

.PARAMETER Database
    The database name. Default: sqldb-noshow

.PARAMETER ProjectPrincipalName
    The name of the AI Foundry project (used as the external login name).
    This is the project resource name, e.g., "proj-noshow-dev-ncus-001"

.EXAMPLE
    .\grant-sql-access.ps1 -SqlServer sql-noshow-dev-ncus-001 -ProjectPrincipalName proj-noshow-dev-ncus-001
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$SqlServer,

    [Parameter(Mandatory = $false)]
    [string]$Database = "sqldb-noshow",

    [Parameter(Mandatory = $false)]
    [string]$ProjectPrincipalName
)

# Helper to get azd environment values
function Get-EnvValue([string]$name) {
    try {
        $value = azd env get-value $name 2>$null
        if ($LASTEXITCODE -eq 0 -and $value) {
            return $value.Trim()
        }
    }
    catch { }
    return $null
}

# Get values from azd env if not provided
if (-not $SqlServer) {
    $SqlServer = Get-EnvValue "AZURE_SQL_SERVER"
    if (-not $SqlServer) {
        $fqdn = Get-EnvValue "AZURE_SQL_SERVER_FQDN"
        if ($fqdn) {
            $SqlServer = $fqdn -replace "\.database\.windows\.net$", ""
        }
    }
}

if (-not $ProjectPrincipalName) {
    $ProjectPrincipalName = Get-EnvValue "AZURE_AI_PROJECT_NAME"
}

if (-not $SqlServer) {
    Write-Error "SQL Server name not provided and not found in azd env. Use -SqlServer parameter."
    exit 1
}

if (-not $ProjectPrincipalName) {
    Write-Error "Project name not provided and not found in azd env. Use -ProjectPrincipalName parameter."
    exit 1
}

$serverFqdn = if ($SqlServer -match "\.database\.windows\.net$") { $SqlServer } else { "$SqlServer.database.windows.net" }

Write-Host "=== Grant SQL Access to AI Foundry Project Identity ===" -ForegroundColor Cyan
Write-Host "SQL Server:    $serverFqdn" -ForegroundColor Gray
Write-Host "Database:      $Database" -ForegroundColor Gray
Write-Host "Project Name:  $ProjectPrincipalName" -ForegroundColor Gray
Write-Host ""

# Get access token for Azure SQL using current user's credentials
Write-Host "Getting access token for Azure SQL..." -ForegroundColor Yellow
$tokenResponse = az account get-access-token --resource "https://database.windows.net/" --query accessToken -o tsv

if ($LASTEXITCODE -ne 0 -or -not $tokenResponse) {
    Write-Error "Failed to get access token. Make sure you're logged in with 'az login'."
    exit 1
}

# SQL to create user and grant permissions
# The external provider name should match the managed identity name (project name)
$sql = @"
-- Drop user if exists (for idempotency)
IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = '$ProjectPrincipalName')
BEGIN
    DROP USER [$ProjectPrincipalName];
    PRINT 'Dropped existing user: $ProjectPrincipalName';
END

-- Create contained database user from external provider (Azure AD/Entra ID)
CREATE USER [$ProjectPrincipalName] FROM EXTERNAL PROVIDER;
PRINT 'Created user: $ProjectPrincipalName';

-- Grant db_datareader role for SELECT permissions
ALTER ROLE db_datareader ADD MEMBER [$ProjectPrincipalName];
PRINT 'Added to db_datareader role';

-- Grant db_datawriter role for INSERT/UPDATE/DELETE (if needed for future)
-- ALTER ROLE db_datawriter ADD MEMBER [$ProjectPrincipalName];

PRINT 'SQL access granted successfully!';
"@

Write-Host "Executing SQL to create user and grant permissions..." -ForegroundColor Yellow

# Use sqlcmd or Invoke-Sqlcmd if available, otherwise use .NET
try {
    # Check if SqlServer module is available
    if (Get-Module -ListAvailable -Name SqlServer) {
        Import-Module SqlServer -ErrorAction Stop
        
        Invoke-Sqlcmd -ServerInstance $serverFqdn `
                      -Database $Database `
                      -AccessToken $tokenResponse `
                      -Query $sql `
                      -Verbose
    }
    else {
        # Fallback: Use .NET SqlConnection
        Write-Host "Using .NET SqlConnection (SqlServer module not found)..." -ForegroundColor Gray
        
        Add-Type -AssemblyName "System.Data"
        
        $connectionString = "Server=tcp:$serverFqdn,1433;Database=$Database;Encrypt=True;TrustServerCertificate=False;"
        $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
        $connection.AccessToken = $tokenResponse
        
        try {
            $connection.Open()
            Write-Host "Connected to database." -ForegroundColor Green
            
            $command = $connection.CreateCommand()
            $command.CommandText = $sql
            $command.CommandTimeout = 60
            
            $null = $command.ExecuteNonQuery()
            Write-Host "SQL executed successfully." -ForegroundColor Green
        }
        finally {
            $connection.Close()
        }
    }
    
    Write-Host ""
    Write-Host "SUCCESS: '$ProjectPrincipalName' now has db_datareader access to $Database" -ForegroundColor Green
    Write-Host ""
    Write-Host "The hosted agent can now query appointment data using managed identity authentication." -ForegroundColor Cyan
}
catch {
    Write-Error "Failed to execute SQL: $_"
    Write-Host ""
    Write-Host "Troubleshooting:" -ForegroundColor Yellow
    Write-Host "  1. Ensure you are the SQL admin (Entra ID admin on the server)" -ForegroundColor Gray
    Write-Host "  2. Verify the project name is correct: $ProjectPrincipalName" -ForegroundColor Gray
    Write-Host "  3. Check the project has a system-assigned managed identity" -ForegroundColor Gray
    exit 1
}
