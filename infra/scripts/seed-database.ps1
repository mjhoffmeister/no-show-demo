#Requires -Version 7.0
<#
.SYNOPSIS
    Seeds the Azure SQL Database with schema and synthetic data.

.DESCRIPTION
    This script creates the database schema and loads synthetic data from parquet files.
    Uses Azure AD authentication via az CLI token.
    
    Prerequisites:
    - Azure CLI installed and logged in (az login)
    - Python 3.10+ with pandas and pyarrow in ml/.venv
    - Network access to Azure SQL Database

.PARAMETER Server
    SQL Server hostname (e.g., sql-noshow-dev-ncus-001.database.windows.net)

.PARAMETER Database
    Database name (default: sqldb-noshow)

.PARAMETER ResourceGroup
    Azure resource group containing the SQL server

.PARAMETER SchemaOnly
    Only create schema, skip data seeding

.PARAMETER DataOnly
    Only seed data, skip schema creation (assumes schema exists)

.PARAMETER DataDir
    Directory containing parquet files (default: ml/data/synthetic)

.EXAMPLE
    ./seed-database.ps1 -Server sql-noshow-dev-ncus-001.database.windows.net
    
.EXAMPLE
    ./seed-database.ps1 -Server sql-noshow-dev-ncus-001.database.windows.net -SchemaOnly

.EXAMPLE
    ./seed-database.ps1 -Server sql-noshow-dev-ncus-001.database.windows.net -DataOnly
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$Server = "sql-noshow-dev-ncus-001.database.windows.net",
    
    [Parameter(Mandatory = $false)]
    [string]$Database = "sqldb-noshow",
    
    [Parameter(Mandatory = $false)]
    [string]$ResourceGroup = "rg-noshow-dev-ncus-001",
    
    [Parameter(Mandatory = $false)]
    [switch]$SchemaOnly,
    
    [Parameter(Mandatory = $false)]
    [switch]$DataOnly,
    
    [Parameter(Mandatory = $false)]
    [string]$DataDir = ""
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

if (-not $DataDir) {
    $DataDir = Join-Path $RepoRoot "ml\data\synthetic"
}

# =============================================================================
# Helper Functions
# =============================================================================

function Write-Step {
    param([string]$Message)
    Write-Host "`n=== $Message ===" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "✓ $Message" -ForegroundColor Green
}

function Write-Info {
    param([string]$Message)
    Write-Host "  $Message" -ForegroundColor Gray
}

function Get-AzureSqlToken {
    <#
    .SYNOPSIS
        Gets an access token for Azure SQL Database using az CLI.
    #>
    Write-Info "Acquiring Azure AD token..."
    $token = az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to get Azure AD token. Run 'az login' first."
    }
    return $token
}

function New-SqlConnection {
    <#
    .SYNOPSIS
        Creates a new SQL connection with Azure AD token authentication.
    #>
    param(
        [string]$Server,
        [string]$Database,
        [string]$Token
    )
    
    $connectionString = "Server=$Server;Database=$Database;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
    
    # Use System.Data.SqlClient which supports AccessToken
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.AccessToken = $Token
    $connection.Open()
    return $connection
}

function Invoke-SqlBatch {
    <#
    .SYNOPSIS
        Executes a SQL batch against the database.
    #>
    param(
        [object]$Connection,
        [string]$Sql,
        [int]$Timeout = 120
    )
    
    $cmd = $Connection.CreateCommand()
    $cmd.CommandText = $Sql
    $cmd.CommandTimeout = $Timeout
    $cmd.ExecuteNonQuery() | Out-Null
}

function Invoke-SqlQuery {
    <#
    .SYNOPSIS
        Executes a SQL query and returns results.
    #>
    param(
        [object]$Connection,
        [string]$Sql
    )
    
    $cmd = $Connection.CreateCommand()
    $cmd.CommandText = $Sql
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter($cmd)
    $dataset = New-Object System.Data.DataSet
    $adapter.Fill($dataset) | Out-Null
    return $dataset.Tables[0]
}

# =============================================================================
# Schema Creation
# =============================================================================

function Install-Schema {
    <#
    .SYNOPSIS
        Creates or updates the database schema from schema.sql.
    #>
    param(
        [object]$Connection
    )
    
    Write-Step "Creating Database Schema"
    
    $schemaFile = Join-Path $RepoRoot "infra\modules\sql\schema.sql"
    if (-not (Test-Path $schemaFile)) {
        throw "Schema file not found: $schemaFile"
    }
    
    Write-Info "Reading schema from $schemaFile"
    $schema = Get-Content -Path $schemaFile -Raw
    
    # Split by GO statements (SQL Server batch separator)
    $batches = $schema -split '(?m)^\s*GO\s*$'
    
    $successCount = 0
    $skipCount = 0
    $errorCount = 0
    
    foreach ($batch in $batches) {
        $batch = $batch.Trim()
        if ($batch -and $batch.Length -gt 10) {
            try {
                Invoke-SqlBatch -Connection $Connection -Sql $batch
                $successCount++
                
                # Extract object name for logging
                if ($batch -match "CREATE\s+(TABLE|INDEX|VIEW)\s+(\S+)") {
                    Write-Info "Created $($Matches[1]): $($Matches[2])"
                }
            }
            catch {
                $errorMsg = $_.Exception.Message
                if ($errorMsg -match "already exists") {
                    $skipCount++
                    if ($batch -match "CREATE\s+(TABLE|INDEX|VIEW)\s+(\S+)") {
                        Write-Info "Skipped (exists): $($Matches[2])"
                    }
                }
                else {
                    $errorCount++
                    Write-Warning "Error: $errorMsg"
                }
            }
        }
    }
    
    Write-Success "Schema complete: $successCount created, $skipCount skipped, $errorCount errors"
}

# =============================================================================
# Data Seeding
# =============================================================================

function Clear-Tables {
    <#
    .SYNOPSIS
        Truncates all tables before loading fresh data.
    #>
    param(
        [object]$Connection
    )
    
    Write-Info "Clearing existing data..."
    
    # Order matters due to foreign key constraints
    $tables = @("Predictions", "Appointments", "Insurance", "Patients", "Providers", "Departments")
    
    foreach ($table in $tables) {
        try {
            Invoke-SqlBatch -Connection $Connection -Sql "DELETE FROM dbo.$table"
            Write-Info "  Cleared $table"
        }
        catch {
            Write-Info "  Skipped $table (may not exist)"
        }
    }
}

function Import-ParquetData {
    <#
    .SYNOPSIS
        Imports data from parquet files using Python.
    #>
    param(
        [string]$Server,
        [string]$Database,
        [string]$DataDir,
        [string]$Token
    )
    
    Write-Step "Seeding Data from Parquet Files"
    
    # Verify parquet files exist
    $requiredFiles = @("patients.parquet", "providers.parquet", "departments.parquet", "insurance.parquet", "appointments.parquet")
    foreach ($file in $requiredFiles) {
        $filePath = Join-Path $DataDir $file
        if (-not (Test-Path $filePath)) {
            throw "Required parquet file not found: $filePath"
        }
    }
    Write-Info "Found all required parquet files in $DataDir"
    
    # Find Python executable
    $venvPath = Join-Path $RepoRoot "ml\.venv"
    if (Test-Path $venvPath) {
        $pythonExe = Join-Path $venvPath "Scripts\python.exe"
    }
    else {
        $pythonExe = "python"
    }
    
    # Run the seed_database.py script from ml/src/data
    $seedScript = Join-Path $RepoRoot "ml\src\data\seed_database.py"
    
    Write-Info "Running Python seeding script..."
    & $pythonExe -u $seedScript `
        --server $Server `
        --database $Database `
        --data-dir $DataDir
    
    if ($LASTEXITCODE -ne 0) {
        throw "Python seeding script failed with exit code $LASTEXITCODE"
    }
    
    Write-Success "Data seeding complete"
}

function Test-DataIntegrity {
    <#
    .SYNOPSIS
        Validates the seeded data.
    #>
    param(
        [object]$Connection
    )
    
    Write-Step "Validating Data"
    
    $tables = @("Departments", "Providers", "Patients", "Insurance", "Appointments")
    
    foreach ($table in $tables) {
        try {
            $result = Invoke-SqlQuery -Connection $Connection -Sql "SELECT COUNT(*) as cnt FROM dbo.$table"
            $count = $result.Rows[0].cnt
            Write-Info "$table`: $count records"
        }
        catch {
            Write-Warning "Could not count $table`: $_"
        }
    }
    
    # Check no-show rate
    try {
        $result = Invoke-SqlQuery -Connection $Connection -Sql @"
SELECT 
    COUNT(*) as total,
    SUM(CASE WHEN appointmentstatus = 'No Show' THEN 1 ELSE 0 END) as no_shows
FROM dbo.Appointments
WHERE appointmentdate < GETDATE()
"@
        $total = $result.Rows[0].total
        $noShows = $result.Rows[0].no_shows
        if ($total -gt 0) {
            $rate = [math]::Round(($noShows / $total) * 100, 1)
            Write-Info "Historical no-show rate: $rate% ($noShows / $total)"
        }
    }
    catch {
        Write-Warning "Could not calculate no-show rate"
    }
    
    Write-Success "Validation complete"
}

# =============================================================================
# Main Execution
# =============================================================================

try {
    Write-Host "`n╔════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║           No-Show Predictor Database Seeding               ║" -ForegroundColor Cyan
    Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
    
    Write-Info "Server: $Server"
    Write-Info "Database: $Database"
    Write-Info "Data Directory: $DataDir"
    Write-Info "Mode: $(if ($SchemaOnly) { 'Schema Only' } elseif ($DataOnly) { 'Data Only' } else { 'Full (Schema + Data)' })"
    
    # Get Azure AD token
    $token = Get-AzureSqlToken
    Write-Success "Acquired Azure AD token"
    
    # Connect to database
    Write-Step "Connecting to Database"
    $connection = New-SqlConnection -Server $Server -Database $Database -Token $token
    Write-Success "Connected to $Database"
    
    # Create schema (unless DataOnly)
    if (-not $DataOnly) {
        Install-Schema -Connection $connection
    }
    
    # Seed data (unless SchemaOnly)
    if (-not $SchemaOnly) {
        # Clear existing data
        Clear-Tables -Connection $connection
        
        # Close connection before Python takes over
        $connection.Close()
        
        # Import data via Python
        Import-ParquetData -Server $Server -Database $Database -DataDir $DataDir -Token $token
        
        # Reconnect for validation
        $connection = New-SqlConnection -Server $Server -Database $Database -Token $token
        
        # Validate
        Test-DataIntegrity -Connection $connection
    }
    
    # Cleanup
    $connection.Close()
    
    Write-Host "`n╔════════════════════════════════════════════════════════════╗" -ForegroundColor Green
    Write-Host "║                    Seeding Complete!                       ║" -ForegroundColor Green
    Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Green
}
catch {
    Write-Host "`n╔════════════════════════════════════════════════════════════╗" -ForegroundColor Red
    Write-Host "║                      Seeding Failed                        ║" -ForegroundColor Red
    Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Red
    Write-Host "Error: $_" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkRed
    exit 1
}
