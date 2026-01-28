# Research: Medical Appointment No-Show Predictor

**Feature Branch**: `001-no-show-predictor`  
**Date**: 2026-01-28  
**Plan**: [plan.md](plan.md)

## Overview

This document captures research findings for all technical decisions and unknowns identified during planning. All NEEDS CLARIFICATION items have been resolved.

---

## Technology Decisions

### 1. Azure AI Foundry Hosted Agents (.NET)

**Decision**: Use `Microsoft.Agents.AI` 1.0.0-preview.260127.1 with Microsoft Agent Framework

**Rationale**: 
- Official Microsoft solution for hosting agents in Azure AI Foundry
- Managed infrastructure (autoscaling, conversation state, identity management)
- Native .NET support with hosting adapter pattern
- Integration with Foundry tools, traces, and Application Insights

**Alternatives Considered**:
- LangGraph (Python only for hosted agents)
- Custom agent code without framework (more boilerplate, no built-in patterns)
- Self-hosted agent (lose managed conversation state, scaling, observability)

**Package Versions** (verified 2026-01-28):
| Package | Version | Notes |
|---------|---------|-------|
| `Microsoft.Agents.AI` | 1.0.0-preview.260127.1 | Agent Framework, .NET 8.0+ target |
| `Azure.AI.Projects` | 1.1.0 | Client for agent invocation |
| `Azure.Identity` | latest | DefaultAzureCredential |
| `Microsoft.Extensions.AI` | per Agents.AI deps | AI extensions |

**Key Patterns**:
```csharp
// Agent with tools
var agent = new ChatClientAgent(chatClient,
    instructions: "...",
    tools: [AIFunctionFactory.Create(GetPrediction)])
    .AsBuilder()
    .UseOpenTelemetry(sourceName: "NoShowAgent")
    .Build();

// Run with hosting adapter
await agent.RunAIAgentAsync(telemetrySourceName: "NoShowAgent");
```

**Deployment Pattern**:
1. Containerize agent code with Dockerfile
2. Push to Azure Container Registry
3. Use `azd ai agent init` and `azd up` for deployment
4. Foundry manages hosting, scaling, and conversation state

---

### 2. Azure ML AutoML for No-Show Prediction

**Decision**: Use Azure Machine Learning AutoML for classification model training

**Rationale**:
- Automates feature engineering, algorithm selection, and hyperparameter tuning
- Provides model explainability (feature importance) out-of-the-box
- Integrates with MLflow for experiment tracking (constitution requirement)
- Managed online endpoints for real-time inference

**Alternatives Considered**:
- Manual scikit-learn pipeline (more control, more effort, less consistent)
- Azure OpenAI fine-tuning (not appropriate for tabular classification)
- Pre-built no-show models (none available that fit demo requirements)

**Configuration**:
```yaml
# AutoML classification job
task: classification
primary_metric: AUC_weighted
training_data: azureml:noshow-training:1
target_column_name: no_show
enable_model_explainability: true
experiment_name: noshow-prediction
compute: azureml:cpu-cluster
```

**Key Considerations**:
- Use stratified split to handle class imbalance (typically ~20% no-show rate)
- Guard against data leakage: no post-appointment features in training
- Track experiments with MLflow per constitution requirements

---

### 2.1 Python Version and Dependencies

**Decision**: Python 3.11 with pinned dependencies using `azure-ai-ml` v2 SDK

**Rationale**:
- Python 3.11 is stable, performant, and widely supported by all required packages
- `azure-ai-ml` is the v2 SDK (replacing deprecated `azureml-sdk` v1)
- All versions pinned per constitution requirement (Scientific Rigor - pin all dependency versions)

**Python Version**: 3.11.x (verified compatible with all packages)

**Pinned Dependencies** (verified 2026-01-28):

| Package | Version | Purpose |
|---------|---------|----------|
| `azure-ai-ml` | 1.31.0 | Azure ML v2 SDK (AutoML, endpoints, datasets) |
| `azure-identity` | 1.25.1 | DefaultAzureCredential for credential-less auth |
| `mlflow` | 3.8.1 | Experiment tracking, model logging |
| `pandas` | 3.0.0 | Data manipulation |
| `pyarrow` | 23.0.0 | Parquet I/O, efficient data storage |
| `numpy` | 2.4.1 | Numerical operations |
| `scikit-learn` | 1.8.0 | Data preprocessing, metrics |
| `faker` | 40.1.2 | Synthetic data generation |
| `pyodbc` | 5.3.0 | Azure SQL Database connectivity |
| `python-dotenv` | 1.2.1 | Local environment config loading |
| `pytest` | 9.0.2 | Testing framework |
| `msal` | 1.34.0 | Azure AD authentication (transitive) |

**requirements.txt**:
```text
# Azure ML v2 SDK
azure-ai-ml==1.31.0
azure-identity==1.25.1
mlflow==3.8.1

# Data processing
pandas==3.0.0
pyarrow==23.0.0
numpy==2.4.1
scikit-learn==1.8.0

# Synthetic data generation
faker==40.1.2

# Database connectivity
pyodbc==5.3.0

# Utilities
python-dotenv==1.2.1

# Testing
pytest==9.0.2
```

**Note on azureml-sdk**: The v1 SDK (`azureml-sdk`) is deprecated. Use `azure-ai-ml` for all new development. AutoML is fully supported in v2.

---

### 3. Blazor WebAssembly Frontend

**Decision**: Blazor WASM with MudBlazor component library

**Rationale**:
- C# end-to-end (agent backend + frontend)
- Modern SPA experience without JavaScript framework
- MudBlazor provides Material Design components with dark/light theme
- Direct HTTP calls to Foundry API from browser

**Alternatives Considered**:
- React/Vue/Angular (adds technology diversity, team skill gap)
- Blazor Server (server dependency, WebSocket complexity for SWA)
- MAUI Hybrid (overkill for demo web app)

**UI Requirements**:
- Chat interface with message history
- Auto-scroll to latest message
- Dark/light mode toggle (MudBlazor ThemeProvider)
- Loading indicators during agent response
- Simple, clean aesthetic

**Package Versions**:
| Package | Version | Notes |
|---------|---------|-------|
| `MudBlazor` | 7.x | UI components |
| `Microsoft.AspNetCore.Components.WebAssembly` | 10.x | .NET 10 WASM runtime |

---

### 4. Infrastructure - Terraform AzApi Provider

**Decision**: Use Terraform AzApi provider version 2.8.0 for all Azure resources

**Rationale**:
- Direct access to Azure ARM API (latest resource types, preview features)
- Supports AI Foundry resource types not yet in AzureRM
- Required for capability hosts, hosted agents, and connections

**Alternatives Considered**:
- AzureRM provider (incomplete AI Foundry support)
- Bicep (good, but project standardized on Terraform)
- Azure CLI only (not infrastructure-as-code)

**Key Resources**:
| Resource | API Version | Notes |
|----------|-------------|-------|
| Cognitive Services account (Foundry) | 2025-10-01-preview | Capability host |
| Foundry Project | 2025-10-01-preview | Agent hosting |
| Azure ML Workspace | 2024-10-01 | Model training/hosting |
| Static Web App | 2024-04-01 | Frontend hosting |
| Container Registry | 2023-07-01 | Agent images |

---

### 5. Deployment - Azure Developer CLI (azd)

**Decision**: Use `azd` with `azd ai agent` extension for deployment

**Rationale**:
- Native integration with hosted agents workflow
- Automates ACR build/push, capability host, agent registration
- Provisions Application Insights, managed identity, RBAC
- Single `azd up` command for full deployment

**Workflow**:
```bash
# Initialize with Foundry starter template
azd init -t https://github.com/Azure-Samples/azd-ai-starter-basic

# Configure agent
azd ai agent init -m ./src/agent/agent.yaml

# Deploy everything
azd up

# Teardown
azd down
```

---

### 6. Authentication Pattern

**Decision**: Credential-less authentication using DefaultAzureCredential and Managed Identity

**Rationale**:
- Constitution requirement (Security-First principle)
- No secrets in code, config, or environment variables
- Managed Identity for agent-to-Azure service calls
- User authentication via Entra ID for published agent access

**Implementation**:
```csharp
// Agent code - ML endpoint call
var credential = new DefaultAzureCredential();
var mlClient = new MLOnlineEndpointClient(endpoint, credential);

// Frontend - calls Foundry agent
// Authenticated via Foundry's built-in RBAC
```

---

## Domain Research

### 7. Medical No-Show Risk Factors

**Decision**: Use evidence-based features from healthcare research

**Key Features** (from published literature):
| Feature | Type | Impact | Source |
|---------|------|--------|--------|
| Previous no-show count | Numeric | High | Multiple studies |
| Lead time (days scheduled ahead) | Numeric | Medium | Predictive Analytics in Healthcare |
| Age | Numeric | Low-Medium | Varies by population |
| Insurance type | Categorical | Medium | Access barriers |
| Appointment type (new vs. follow-up) | Categorical | Medium | Engagement factor |
| Day of week | Categorical | Low | Monday/Friday higher |
| Time of day | Categorical | Low | Early morning lower |
| Distance from clinic | Numeric | Medium | Transportation barrier |
| Weather forecast | Reserved | Low | Not implemented for demo |

**Rationale**: Using evidence-based features ensures the demo has face validity for healthcare domain experts.

---

### 8. Synthetic Data Generation

**Decision**: Generate synthetic data with realistic distributions matching published no-show rates, aligned with production Athena/Epic schema

**Target Statistics**:
- Overall no-show rate: 20-25% (typical outpatient)
- Patients: 5,000 synthetic records
- Providers: 100 records (various specialties)
- Departments: 40 records
- Appointments: 100,000 records (mix of past and future)
- Time range: 24 months historical + 2 weeks future (captures full seasonality cycle)

**Data Generation Rules**:
1. **Patient demographics**: Realistic distributions per data-model.md
2. **No-show patterns**: Correlated with evidence-based risk factors
3. **Appointment scheduling**: Business hours, realistic lead times
4. **No PII**: All names, IDs are synthetic/fake
5. **Schema alignment**: Field names match production Athena/Epic schema
6. **Patient journeys**: Generate realistic care sequences (initial visit → follow-up → specialist referral → ongoing care)
7. **Seasonality**: Model monthly/seasonal variation (higher no-shows in winter holidays, summer vacations)
8. **Journey dropout**: Some patients show progressive no-show chains leading to care abandonment

**Schema Reference**: See [data-model.md](data-model.md) for complete production-aligned schema with:
- Patient: `patientid`, `enterpriseid`, `patient_gender`, `patient_age_bucket`, `patient_zip_code`, `portal_last_login`
- Provider: `providerid`, `providerfirstname`, `providerlastname`, `providertype`, `provider_specialty`
- Department: `departmentid`, `departmentname`, `departmentspecialty`, `placeofservicetype`, `market`
- Appointment: `appointmentid`, `appointmentdate`, `appointmentstarttime`, `appointmentstatus`, `appointmenttypename`, etc.
- Insurance: `primarypatientinsuranceid`, `sipg1`, `sipg2`

---

### 9. GPT Model Deployment

**Decision**: Deploy GPT-4o model in Azure AI Foundry project for agent reasoning

**Rationale**:
- Required for agent to process natural language and generate responses
- GPT-4o provides best balance of capability and latency
- Deployed within same Foundry project as hosted agent for simplified auth

**Configuration**:
| Setting | Value |
|---------|-------|
| Model | gpt-4o |
| Deployment Name | gpt-4o |
| API Version | 2024-12-01-preview |
| Capacity | 30K TPM (demo scale) |

**Infrastructure**:
```hcl
# In infra/modules/foundry/main.tf
resource "azapi_resource" "model_deployment" {
  type      = "Microsoft.CognitiveServices/accounts/deployments@2024-10-01"
  name      = "gpt-4o"
  parent_id = azapi_resource.foundry_account.id
  body = {
    sku = {
      name     = "Standard"
      capacity = 30
    }
    properties = {
      model = {
        format  = "OpenAI"
        name    = "gpt-4o"
        version = "2024-11-20"
      }
    }
  }
}
```

---

### 10. Data Storage for Agent Queries

**Decision**: Use Azure SQL Database for appointment/patient data queried by agent

#### Comparison Matrix

| Criteria | Azure SQL Database | Azure PostgreSQL Flexible | Azure Cosmos DB (NoSQL) |
|----------|-------------------|---------------------------|-------------------------|
| **Query Model** | Full T-SQL, complex JOINs | Full SQL, complex JOINs | Document queries, limited JOINs |
| **Schema Fit** | ✅ Exact match to Athena/Epic relational schema | ✅ Excellent relational fit | ⚠️ Requires denormalization |
| **Managed Identity** | ✅ Microsoft Entra (native) | ✅ Microsoft Entra (native) | ✅ RBAC (native) |
| **.NET Data Access** | ADO.NET, Dapper, EF Core | Npgsql, Dapper, EF Core | Azure.Cosmos SDK |
| **Demo-Scale Pricing** | **~$5/mo** (Basic 5 DTU) | ~$13/mo (Burstable B1ms) | ~$25/mo (400 RU/s serverless) |
| **North Central US** | ✅ Available | ✅ Available | ✅ Available |
| **Complex Date Queries** | ✅ Excellent (T-SQL) | ✅ Excellent (PostgreSQL) | ⚠️ Requires partition design |
| **Patient Journey Queries** | ✅ Self-JOINs, window functions | ✅ Self-JOINs, window functions | ⚠️ Multiple round trips |
| **Aggregations** | ✅ GROUP BY, HAVING | ✅ GROUP BY, HAVING | ⚠️ Limited aggregation pipeline |
| **Terraform AzApi** | ✅ Full support | ✅ Full support | ✅ Full support |

#### Use Case Analysis

**Our Query Patterns**:
1. "Get appointments for tomorrow with patient details" → JOIN Patient, Appointment, Provider
2. "Get patient's appointment history" → Appointment WHERE patientid = ? ORDER BY date
3. "Weekly aggregation by day" → GROUP BY DATEPART(dw, appointmentdate)
4. "Provider no-show rates" → Subquery with COUNT/SUM aggregation
5. "Patient journey sequences" → Self-JOIN or window function (LAG/LEAD)

**Why Azure SQL Wins**:
- Production Athena/Epic schema is relational with normalized tables
- All 5 query patterns are natural T-SQL (no workarounds)
- Lowest cost for demo scale ($5/mo vs $13-25/mo)
- Constitution alignment: simplest solution that works (KISS)

**Why NOT Cosmos DB**:
- Cross-document JOINs not supported - must either denormalize OR do multiple round-trips
- Our query patterns are JOIN-heavy (5 of 5 patterns require multi-entity queries)
- Overkill for demo scale (<100K records, single region)
- Request Unit (RU) pricing model harder to predict

**Why NOT PostgreSQL**:
- Slightly higher cost ($13/mo vs $5/mo) for equivalent capability
- No clear advantage over Azure SQL for this use case
- T-SQL syntax more common in .NET ecosystem

#### Decision Rationale

| Factor | Weight | Winner |
|--------|--------|--------|
| Schema compatibility | High | SQL = PostgreSQL > Cosmos |
| Query complexity support | High | SQL = PostgreSQL > Cosmos |
| Cost (demo scale) | Medium | SQL ($5) > PostgreSQL ($13) > Cosmos ($25) |

**Final Decision**: Azure SQL Database (Basic tier, 5 DTU)

#### Configuration

| Setting | Value |
|---------|-------|
| SKU | Basic (5 DTU) |
| Max Size | 2 GB |
| Authentication | Managed Identity only |
| Backup | Locally redundant (demo) |
| Collation | SQL_Latin1_General_CP1_CI_AS |

#### Agent Access Pattern

Data access approach is implementation choice (ADO.NET, Dapper, EF Core, etc.).

```csharp
// Connection string uses Managed Identity
"Server=tcp:{server}.database.windows.net;Database=noshow;Authentication=Active Directory Managed Identity;"
```

---

### 11. Agent System Prompt

**Decision**: Define structured system prompt for scheduling coordinator persona

**System Prompt**:
```text
You are a scheduling coordinator assistant for a healthcare clinic. Your role is to help staff identify patients at risk of missing their appointments and recommend actions to reduce no-shows.

## Capabilities
- Query appointment schedules for specific dates or date ranges
- Retrieve no-show probability predictions for upcoming appointments
- Explain risk factors contributing to high no-show predictions
- Recommend actions like confirmation calls, reminders, or overbooking strategies
- Look up individual patient appointment history and risk profiles

## Guidelines
1. Always include the no-show probability percentage when discussing appointment risk
2. Prioritize recommendations by impact (high-risk patients first)
3. When listing appointments, include: patient name, time, provider, and risk level
4. Explain risk factors in plain language (e.g., "Patient has missed 3 of last 5 appointments")
5. For date queries, confirm the interpreted date in your response
6. If predictions are unavailable, indicate this and provide historical data instead

## Data Access
You have access to:
- Appointment schedules (past and future)
- Patient demographics and history
- Provider schedules
- ML model predictions for no-show probability

## Constraints
- Never reveal raw patient IDs; use names only
- All data is synthetic for demonstration purposes
- Predictions are estimates; recommend verification for critical decisions
```

**Tools Available to Agent**:
| Tool | Purpose |
|------|---------|
| `get_appointments` | Query appointments by date range, provider, or patient |
| `get_prediction` | Get ML no-show probability for specific appointment(s) |
| `get_patient_history` | Retrieve patient's past appointment attendance |
| `get_recommendations` | Generate action recommendations for high-risk appointments |

---

## Unresolved Items

**None** - All technical decisions have been resolved.

---

## References

1. Azure AI Foundry Hosted Agents Documentation (fetched 2026-01-28)
2. Microsoft.Agents.AI NuGet (v1.0.0-preview.260127.1)
3. Azure.AI.Projects NuGet (v1.1.0)
4. Azure ML AutoML Classification Documentation
5. MudBlazor Documentation (v7.x)
6. Terraform AzApi Provider 2.8.0 Documentation
7. Healthcare no-show literature (various sources)
