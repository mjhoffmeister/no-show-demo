param(
    [Parameter(Mandatory = $false)]
    [string]$UserPrincipalName,

    [Parameter(Mandatory = $false)]
    [string]$ObjectId,

    [Parameter(Mandatory = $false)]
    [string]$Scope
)

function Get-EnvValue([string]$name) {
    try {
        $value = azd env get-value $name 2>$null
        if ($LASTEXITCODE -eq 0 -and $value) {
            return $value
        }
    }
    catch {
    }
    return $null
}

if (-not $Scope) {
    $Scope = Get-EnvValue "AZURE_AI_PROJECT_ID"
}

if (-not $Scope) {
    Write-Error "Scope not provided and AZURE_AI_PROJECT_ID is not set. Provide -Scope with the Foundry project resource ID."
    exit 1
}

if (-not $ObjectId -and -not $UserPrincipalName) {
    Write-Error "Provide -UserPrincipalName or -ObjectId."
    exit 1
}

$principalType = "User"
if (-not $ObjectId) {
    try {
        $ObjectId = az ad user show --id $UserPrincipalName --query id -o tsv 2>$null
    }
    catch {
    }

    if (-not $ObjectId) {
        try {
            $ObjectId = az ad sp show --id $UserPrincipalName --query id -o tsv 2>$null
            if ($ObjectId) {
                $principalType = "ServicePrincipal"
            }
        }
        catch {
        }
    }
}

if (-not $ObjectId) {
    Write-Error "Could not resolve ObjectId for '$UserPrincipalName'."
    exit 1
}

Write-Host "Assigning 'Azure AI User' role to principal '$ObjectId' on scope '$Scope'..."

az role assignment create `
    --assignee-object-id $ObjectId `
    --assignee-principal-type $principalType `
    --role "Azure AI User" `
    --scope $Scope | Out-Null

Write-Host "Role assignment complete."
