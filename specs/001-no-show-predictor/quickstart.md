# Quickstart: Medical Appointment No-Show Predictor

**Feature Branch**: `001-no-show-predictor`  
**Time to Complete**: ~45 minutes (first-time setup)  
**Prerequisites**: Azure subscription, Azure CLI, Docker, .NET 10 SDK

## Overview

This guide walks you through deploying the No-Show Predictor demo from scratch:
1. Provision Azure infrastructure with `azd` (uses Terraform)
2. Generate synthetic data and seed the database
3. Train the ML model with Azure AutoML
4. Deploy the hosted agent to Azure AI Foundry
5. Deploy the Blazor frontend to Azure Static Web Apps
6. Test the system end-to-end

---

## Prerequisites

### Required Tools

```powershell
# Verify installations
az --version          # Azure CLI 2.60+
terraform --version   # Terraform 1.5+
docker --version      # Docker 24+
dotnet --version      # .NET 10.x
python --version      # Python 3.11.x (required)
azd version           # Azure Developer CLI 1.9+
```

### Install Missing Tools

```powershell
# Azure Developer CLI
winget install Microsoft.Azd

# Azure CLI extensions
az extension add --name ai
az extension add --name ml
```

### Azure Account Setup

```powershell
# Login to Azure
az login

# Set subscription (replace with your subscription ID)
az account set --subscription "<your-subscription-id>"

# Verify
az account show --query "{name:name, id:id}" -o table
```

---

## Step 1: Clone and Configure

```powershell
# Clone the repository
git clone https://github.com/your-org/no-show-demo.git
cd no-show-demo

# Switch to feature branch
git checkout 001-no-show-predictor

# Initialize azd environment
azd init

# Configure environment (when prompted)
#   Environment name: noshow-dev
#   Azure Subscription: <select your subscription>
#   Azure Location: northcentralus  # Required for hosted agents!
```

---

## Step 2: Provision Infrastructure

```powershell
# Provision all Azure resources
azd provision
```

This runs Terraform and creates:
- Azure AI Foundry account with project and GPT-4o deployment
- Azure ML Workspace with managed online endpoint
- Azure SQL Database (with schema)
- Azure Container Registry
- Azure Static Web App
- Application Insights
- Managed identities with RBAC

**Expected Time**: ~10 minutes

The `postprovision` hook automatically:
- Sets environment variables for all services
- Creates SQL database user for the agent managed identity

---

## Step 3: Generate and Seed Data

```powershell
# Create Python virtual environment
cd ml
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt

# Generate synthetic data
python src/data/generate_synthetic.py

# Seed Azure SQL Database
.\infra\scripts\seed-database.ps1

cd ..
```

**Verification:**
```powershell
# Check record counts
$tok = az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv
# Use Azure Data Studio or similar to verify:
# - Patients: ~1,000 records
# - Appointments: ~15,000 records
```

---

## Step 4: Train ML Model

```powershell
cd ml

# Submit AutoML training job
az ml job create -f src/training/automl_job.yaml --workspace-name <ml-workspace>

# Monitor training (~30 minutes)
az ml job show --name <job-name> --query "status"

# Deploy best model to managed endpoint
az ml online-deployment create -f deployment/deployment.yaml

# Test the endpoint
python src/evaluation/test_endpoint.py

cd ..
```

**Verification:**
- ML model registered in Azure ML
- Online endpoint responding at the URL in `azd env get-value AZURE_ML_ENDPOINT_URI`

---

## Step 5: Deploy Agent

```powershell
# Deploy the hosted agent to Azure AI Foundry
azd deploy agent
```

This builds the agent container, pushes to ACR, and deploys to Foundry's hosted agent infrastructure.

**Verification:**
```powershell
# Check agent status
azd ai agent show
```

---

## Step 6: Deploy Frontend

```powershell
# Deploy to Static Web App (automatically picks up agent endpoint from Step 5)
azd deploy frontend
```

The `prepackage` hook automatically injects `AZURE_AI_AGENT_ENDPOINT` into `appsettings.json` before building.

**Verification:**
```powershell
# Get frontend URL
azd env get-value AZURE_STATIC_WEB_APP_URL
```

---

## Step 7: End-to-End Test

### Open the Application

1. Get the URL: `azd env get-value AZURE_SWA_URL`
2. Navigate to the Static Web App URL
3. The chat interface should load with dark/light mode toggle

### Test Queries

Try these sample queries:

| Query | Expected Response |
|-------|------------------|
| "Which patients are most likely to miss their appointments tomorrow?" | Ranked list of high-risk appointments with probabilities |
| "What scheduling actions should I take for tomorrow?" | Prioritized recommendations (confirmation calls, overbooking suggestions) |
| "What does the no-show forecast look like for this week?" | Daily summary with expected no-show rates |
| "Tell me about patient Maria Garcia's no-show history" | Patient history with risk factors |

### Verify Features

- [ ] Chat messages display correctly
- [ ] Auto-scroll to latest message works
- [ ] Dark/light mode toggle works
- [ ] Agent responses include risk percentages
- [ ] Recommendations are actionable

---

## Troubleshooting

### Agent Not Responding

```powershell
# Check agent deployment status
azd ai agent show

# View agent logs
azd ai agent logs
```

### ML Endpoint Errors

```powershell
# Get endpoint name
$endpoint = azd env get-value AZURE_ML_ENDPOINT_NAME

# Check endpoint status
az ml online-endpoint show --name $endpoint

# View deployment logs
az ml online-deployment get-logs --name noshow-deployment --endpoint $endpoint
```

### Frontend Not Loading

```powershell
# Check Static Web App URL
azd env get-value AZURE_SWA_URL

# Verify agent endpoint is configured
cat frontend/src/NoShowPredictor.Web/appsettings.json
```

---

## Cleanup

```powershell
# Remove all Azure resources
azd down
```

---

## Next Steps

- Review the [architecture documentation](../docs/architecture.md)
- Explore the [API contracts](contracts/)
- Run the [full test suite](../tests/)
- Customize the synthetic data generation parameters
- Experiment with different ML model configurations

---

## Configuration Reference

### Environment Variables (set by azd)

| Variable | Description |
|----------|-------------|
| `AZURE_SUBSCRIPTION_ID` | Azure subscription ID |
| `AZURE_LOCATION` | Azure region (must be `northcentralus` for hosted agents) |
| `AZURE_AI_PROJECT_ENDPOINT` | Foundry project endpoint |
| `AZURE_OPENAI_ENDPOINT` | Azure OpenAI endpoint |
| `AZURE_ML_ENDPOINT_URI` | ML inference scoring endpoint |
| `AZURE_SQL_SERVER` | SQL Server hostname |

### Agent Configuration (agent.yaml)

```yaml
name: noshow-predictor-agent
description: Medical appointment no-show predictor with scheduling recommendations
host:
  type: azure_ai_agent
```

Tools are implemented as C# methods in the agent code, not as separate Foundry resources.
