# Quickstart: Medical Appointment No-Show Predictor

**Feature Branch**: `001-no-show-predictor`  
**Time to Complete**: ~45 minutes (first-time setup)  
**Prerequisites**: Azure subscription, Azure CLI, Docker, .NET 10 SDK

## Overview

This guide walks you through deploying the No-Show Predictor demo from scratch:
1. Provision Azure infrastructure with Terraform
2. Generate synthetic data and train the ML model
3. Deploy the hosted agent to Azure AI Foundry
4. Deploy the Blazor frontend to Azure Static Web Apps
5. Test the system end-to-end

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

# AZD AI Agent extension (auto-installs with template)
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

# Copy environment template
Copy-Item .env.example .env

# Edit .env with your values
# Required:
#   AZURE_SUBSCRIPTION_ID=<your-subscription-id>
#   AZURE_LOCATION=northcentralus  # Required for hosted agents
```

---

## Step 2: Provision Infrastructure

```powershell
# Navigate to infrastructure directory
cd infra

# Initialize Terraform
terraform init

# Preview changes
terraform plan -out=tfplan

# Apply infrastructure
terraform apply tfplan

# Save outputs for later steps
terraform output -json > ../outputs.json
cd ..
```

**Resources Created:**
- Azure AI Foundry resource with project + GPT-4o deployment
- Azure ML Workspace
- Azure SQL Database (Basic tier)
- Azure Container Registry
- Azure Static Web App
- Application Insights
- Storage Account (for ML artifacts)

**Expected Time**: ~10 minutes

---

## Step 3: Generate Synthetic Data

```powershell
# Navigate to ML directory
cd ml/src

# Create Python virtual environment
python -m venv .venv
.\.venv\Scripts\Activate.ps1

# Install dependencies
pip install -r requirements.txt

# Generate synthetic data (5000 patients, 100000 appointments, 24 months for seasonality)
python data/generate_synthetic.py --patients 5000 --appointments 100000 --months 24 --output ../data/

# Upload to Azure Storage
az storage blob upload-batch `
    --account-name $(terraform output -raw storage_account_name -state=../../infra/terraform.tfstate) `
    --destination synthetic-data `
    --source ../data/ `
    --auth-mode login

cd ../..
```

**Output Files:**
- `data/patients.parquet`
- `data/appointments.parquet`
- `data/providers.parquet`
- `data/departments.parquet`
- `data/insurance.parquet`

---

## Step 3b: Seed Azure SQL Database

```powershell
# Still in ml/src directory
# Get SQL connection string from Terraform outputs
$sqlServer = $(terraform output -raw sql_server_name -state=../../infra/terraform.tfstate)

# Seed database with synthetic data (uses Managed Identity)
python data/seed_database.py `
    --server "$sqlServer.database.windows.net" `
    --database noshow `
    --data-dir ../data/

# Verify data loaded
az sql query `
    --server $sqlServer `
    --database noshow `
    --query "SELECT COUNT(*) as patient_count FROM patients; SELECT COUNT(*) as appointment_count FROM appointments;"
```

**Verification:**
- Patients table: ~1,000 records
- Appointments table: ~15,000 records
- Providers table: ~50 records
- Departments table: ~25 records

---

## Step 4: Train ML Model

```powershell
# Navigate to ML directory
cd ml/src

# Submit AutoML training job
python training/train_automl.py `
    --experiment-name noshow-prediction `
    --compute-target cpu-cluster `
    --data-version 1

# Monitor training (takes ~30 minutes)
az ml job show --name <job-name> --query "status" -o tsv

# Register the best model
az ml model create `
    --name noshow-model `
    --version 1 `
    --path azureml://jobs/<job-name>/outputs/best_model

# Deploy to managed endpoint
az ml online-endpoint create --file deployment/endpoint.yaml
az ml online-deployment create --file deployment/deployment.yaml

# Test the endpoint
python evaluation/test_endpoint.py

cd ../..
```

**Verification:**
- ML model registered in Azure ML
- Online endpoint responding at `https://<endpoint>.northcentralus.inference.ml.azure.com/score`

---

## Step 5: Build and Deploy Agent

```powershell
# Navigate to agent directory
cd agent

# Build the agent Docker image
docker build -t noshow-agent:v1 .

# Login to Azure Container Registry
$acrName = $(terraform output -raw acr_name -state=../../infra/terraform.tfstate)
az acr login --name $acrName

# Tag and push
docker tag noshow-agent:v1 "$acrName.azurecr.io/noshow-agent:v1"
docker push "$acrName.azurecr.io/noshow-agent:v1"

# Initialize hosted agent with azd
cd ../..
azd ai agent init -m agent/agent.yaml

# Deploy everything
azd up
```

**Verification:**
```powershell
# Check agent status
az cognitiveservices agent show `
    --account-name <foundry-account> `
    --project-name <project-name> `
    --name noshow-predictor-agent

# Test locally first (optional)
cd agent/src/NoShowPredictor.Agent
dotnet run
# In another terminal: curl http://localhost:8088/responses -X POST -d '{"input":{"messages":[{"role":"user","content":"Hello"}]}}'
```

---

## Step 6: Deploy Frontend

```powershell
# Navigate to frontend
cd frontend/src/NoShowPredictor.Web

# Build Blazor WASM
dotnet publish -c Release -o publish

# Deploy to Static Web App (via azd)
cd ../../..
azd deploy frontend
```

**Verification:**
- Frontend accessible at `https://<swa-name>.azurestaticapps.net`

---

## Step 7: End-to-End Test

### Open the Application

1. Navigate to `https://<swa-name>.azurestaticapps.net`
2. The chat interface should load with dark/light mode toggle

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
az cognitiveservices agent show --account-name <account> --project-name <project> --name noshow-predictor-agent

# View agent logs
curl -N "https://<endpoint>/api/projects/<project>/agents/noshow-predictor-agent/versions/1/containers/default:logstream?kind=console&api-version=2025-11-15-preview" `
    -H "Authorization: Bearer $(az account get-access-token --resource https://ai.azure.com --query accessToken -o tsv)"
```

### ML Endpoint Errors

```powershell
# Check endpoint status
az ml online-endpoint show --name noshow-endpoint

# View deployment logs
az ml online-deployment get-logs --name noshow-deployment --endpoint noshow-endpoint
```

### Frontend Not Loading

```powershell
# Check Static Web App deployment
az staticwebapp show --name <swa-name>

# Check for build errors
az staticwebapp environment show --name <swa-name>
```

---

## Cleanup

```powershell
# Remove all Azure resources
azd down

# Or manually via Terraform
cd infra
terraform destroy
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

### Environment Variables

| Variable | Description | Required |
|----------|-------------|----------|
| `AZURE_SUBSCRIPTION_ID` | Azure subscription ID | Yes |
| `AZURE_LOCATION` | Azure region (must be `northcentralus` for hosted agents) | Yes |
| `AZURE_AI_PROJECT_ENDPOINT` | Foundry project endpoint | Auto-configured |
| `MODEL_DEPLOYMENT_NAME` | GPT model deployment name | Auto-configured |
| `ML_ENDPOINT_URL` | ML inference endpoint | Auto-configured |

### Agent Configuration (agent.yaml)

```yaml
name: noshow-predictor-agent
description: Medical appointment no-show predictor with recommendations
container:
  image: ${ACR_NAME}.azurecr.io/noshow-agent:v1
  cpu: "1"
  memory: "2Gi"
environment:
  AZURE_AI_PROJECT_ENDPOINT: ${FOUNDRY_ENDPOINT}
  MODEL_DEPLOYMENT_NAME: gpt-4o
  ML_ENDPOINT_URL: ${ML_ENDPOINT_URL}
tools:
  - type: code_interpreter
```
