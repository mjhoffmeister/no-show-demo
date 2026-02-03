# No-Show Predictor Agent

Azure AI Foundry Hosted Agent for predicting medical appointment no-shows.

## Architecture

This agent uses the **Azure AI AgentServer SDK** (`Azure.AI.AgentServer.AgentFramework`) 
to host an AI agent that:

1. Queries patient and appointment data from Azure SQL
2. Calls the ML prediction endpoint for no-show probability
3. Provides recommendations to reduce no-shows

## Technology Stack

| Component | Technology |
|-----------|------------|
| Framework | Azure.AI.AgentServer.AgentFramework 1.0.0-beta.6 |
| Runtime | Azure AI Foundry Hosted Agents |
| Language | .NET 10 (C#) |
| Data Access | Microsoft.Data.SqlClient |
| Auth | DefaultAzureCredential (Managed Identity) |

## Infrastructure

The agent uses a **hybrid infrastructure** approach:

| Component | Tool | Location |
|-----------|------|----------|
| SQL, ML, SWA | Terraform | `infra/` |
| Foundry, ACR, CapHost | Bicep | `infra-agent/` |

### Step 1: Deploy Base Infrastructure

From the repo root:

```bash
cd c:\source\no-show-demo
azd auth login
azd up  # Deploys SQL, ML, SWA via Terraform
```

### Step 2: Deploy Hosted Agent

From the infra-agent folder:

```bash
cd c:\source\no-show-demo\infra-agent
azd auth login
azd init  # Select existing environment or create new
azd up    # Deploys Foundry, ACR, CapHost + agent via Bicep
```

## Project Structure

```
agent/
├── src/
│   └── NoShowPredictor.Agent/    # Main agent project
│       └── Program.cs            # Hosted agent entry point
├── Dockerfile                    # Not used - see src/noshow-predictor/
└── README.md
src/noshow-predictor/             # Agent deployment package
├── Dockerfile                    # Container build
├── agent.yaml                    # Agent manifest for azd
└── src/NoShowPredictor.Agent/    # Agent code (symlink/copy)
```

## Environment Variables

The agent reads these from the Foundry environment:

| Variable | Description |
|----------|-------------|
| `AZURE_OPENAI_ENDPOINT` | Azure OpenAI endpoint (auto-set by Foundry) |
| `AZURE_OPENAI_DEPLOYMENT_NAME` | Model deployment name (default: gpt-4o) |
| `SQL_SERVER` | Azure SQL server FQDN |
| `SQL_DATABASE` | Database name |
| `ML_ENDPOINT_URI` | ML prediction endpoint URL |

## Local Development

### Prerequisites

- .NET 10 SDK
- Azure CLI (`az login`)
- Access to Azure OpenAI

### Run Locally

```bash
cd agent/src/NoShowPredictor.Agent

# Set environment variables
$env:AZURE_OPENAI_ENDPOINT = "https://your-openai.openai.azure.com/"
$env:AZURE_OPENAI_DEPLOYMENT_NAME = "gpt-4o"

dotnet run
```

The agent starts on `http://localhost:8088/` with an OpenAI Responses-compatible API.

### Test Request

```http
POST http://localhost:8088/
Content-Type: application/json

{
  "input": "What is the current date?"
}
```

## Related Documentation

- [Specification](../specs/001-no-show-predictor/spec.md)
- [Implementation Plan](../specs/001-no-show-predictor/plan.md)
- [Data Model](../specs/001-no-show-predictor/data-model.md)
