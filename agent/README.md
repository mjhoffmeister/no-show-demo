# No-Show Predictor Agent

.NET 10 hosted agent for the Medical Appointment No-Show Predictor.

## Overview

This agent provides a conversational interface for scheduling coordinators to:
- Query upcoming appointments and their no-show risk levels
- Get actionable recommendations for reducing no-shows
- Explore patient appointment history and risk factors

## Technology Stack

| Component | Technology |
|-----------|------------|
| Framework | Microsoft.Agents.AI 1.0.0-preview.260127.1 |
| Runtime | Azure AI Foundry Hosted Agents |
| Language | .NET 10 (C#) |
| Data Access | Microsoft.Data.SqlClient |
| Auth | DefaultAzureCredential (Managed Identity) |

## Project Structure

```
agent/
├── src/
│   └── NoShowPredictor.Agent/    # Main agent project
│       ├── Program.cs            # Hosting adapter entry point
│       ├── NoShowAgent.cs        # Agent logic and system prompt
│       ├── Tools/                # Agent tools
│       │   ├── PredictionTool.cs
│       │   ├── AppointmentTool.cs
│       │   ├── PatientTool.cs
│       │   └── RecommendationTool.cs
│       ├── Services/             # External service clients
│       │   ├── IMLEndpointClient.cs
│       │   └── MLEndpointClient.cs
│       ├── Data/                 # Data access
│       │   └── AppointmentRepository.cs
│       └── Models/               # Domain entities
├── tests/
│   └── NoShowPredictor.Agent.Tests/
├── Dockerfile
└── README.md
```

## Local Development

### Prerequisites

- .NET 10 SDK
- Azure CLI (`az login`)
- Access to Azure AI Foundry project

### Build & Run

```bash
# Build
dotnet build

# Run locally (requires Azure credentials)
dotnet run --project src/NoShowPredictor.Agent
```

### Environment Variables

| Variable | Description |
|----------|-------------|
| `AZURE_AI_PROJECT_CONNECTION_STRING` | AI Foundry project connection |
| `SQL_CONNECTION_STRING` | Azure SQL Database connection |
| `ML_ENDPOINT_URL` | ML model inference endpoint |

## Deployment

Deployed via `azd up` as a containerized agent in Azure AI Foundry.

```bash
# From repository root
azd up
```

## Testing

```bash
dotnet test
```

## Related Documentation

- [Specification](../specs/001-no-show-predictor/spec.md)
- [Implementation Plan](../specs/001-no-show-predictor/plan.md)
- [Data Model](../specs/001-no-show-predictor/data-model.md)
