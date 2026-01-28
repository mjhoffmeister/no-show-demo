# Implementation Plan: Medical Appointment No-Show Predictor

**Branch**: `001-no-show-predictor` | **Date**: 2026-01-28 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-no-show-predictor/spec.md`

## Summary

Build a demonstration system combining an ML model (Azure AutoML) for no-show probability prediction with a conversational AI agent (Azure AI Foundry Hosted Agent) that allows scheduling coordinators to query upcoming appointment risks and receive actionable recommendations. The frontend is a Blazor WebAssembly chat interface hosted on Azure Static Web Apps. Infrastructure is managed via Terraform AzApi provider and deployed with Azure Developer CLI.

## Technical Context

**Language/Version**: C# / .NET 10 (agent backend), Blazor WebAssembly (frontend), Python 3.11.x (ML training scripts)  
**Primary Dependencies**: 
- Agent: `Microsoft.Agents.AI` 1.0.0-preview.260127.1, `Azure.AI.Projects` 1.1.0, `Azure.Identity`, `Microsoft.Data.SqlClient`
- Frontend: Blazor WebAssembly, MudBlazor 7.x
- ML: `azure-ai-ml` 1.31.0 (v2 SDK), `pandas` 3.0.0, `mlflow` 3.8.1 (see research.md Section 2.1 for full list)
- Infra: Terraform AzApi provider 2.8.0, azd CLI

**Storage**: Azure SQL Database (appointment/patient data), Azure ML datasets (training data), Azure Blob Storage (model artifacts), Foundry conversation state (managed)  
**Testing**: xUnit (.NET), Playwright (frontend E2E), pytest (ML validation)  
**Target Platform**: Azure (North Central US for hosted agents preview), browsers (WASM)  
**Project Type**: Web application (frontend + agent backend + ML pipeline)  
**Performance Goals**: <5s response time for predictions, 70%+ model accuracy  
**Constraints**: North Central US region only (hosted agents preview), synthetic data only (no real PHI)  
**Scale/Scope**: Demo scale (5,000 patients, 100,000 appointments over 24 months, single-user)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Security-First** | ✅ PASS | Managed Identity + DefaultAzureCredential for all Azure services; no secrets in code; synthetic data only |
| **II. Expert Coding Standards** | ✅ PASS | SOLID architecture: separate concerns (ML, Agent, Frontend); DI throughout; interface-based design |
| **III. Clarity Over Cleverness** | ✅ PASS | Explicit service boundaries; descriptive naming conventions; self-documenting API contracts |
| **IV. Self-Documenting Code First** | ✅ PASS | OpenAPI contracts; typed models; XML doc comments for public APIs |
| **V. Scientific Rigor for Data & ML** | ✅ PASS | Azure ML experiment tracking; fixed random seeds; versioned synthetic data; proper train/test split |

**Gate Result**: ✅ PASS - Proceed to Phase 0

## Project Structure

### Documentation (this feature)

```text
specs/001-no-show-predictor/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (OpenAPI specs)
└── tasks.md             # Phase 2 output (NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
agent/                               # .NET 10 Hosted Agent
├── src/
│   └── NoShowPredictor.Agent/       # Agent implementation
│       ├── Program.cs               # Hosting adapter entry point
│       ├── NoShowAgent.cs           # Agent logic
│       ├── Tools/                   # Agent tools (ML inference, data access)
│       │   ├── PredictionTool.cs    # Calls ML endpoint
│       │   └── AppointmentTool.cs   # Queries appointment data
│       └── Services/                # Support services
│           ├── IMLEndpointClient.cs
│           └── MLEndpointClient.cs
├── tests/
│   └── NoShowPredictor.Agent.Tests/ # Agent unit tests
├── Dockerfile                       # Container image for hosted agent
└── README.md

frontend/                            # Blazor WebAssembly
├── src/
│   └── NoShowPredictor.Web/         # Blazor WASM project
│       ├── wwwroot/                 # Static assets
│       ├── Pages/                   # Razor pages
│       │   └── Chat.razor           # Main chat interface
│       ├── Components/              # UI components
│       │   ├── ChatMessage.razor    # Message display
│       │   └── ThemeToggle.razor    # Dark/light mode
│       ├── Services/                # API clients
│       │   └── AgentApiClient.cs    # Foundry agent client
│       └── Program.cs               # WASM entry point
├── tests/
│   └── NoShowPredictor.Web.Tests/   # Frontend tests
└── README.md

ml/                                  # Machine Learning Pipeline
├── src/
│   ├── data/                        # Synthetic data generation
│   │   ├── generate_synthetic.py   # Data generator script
│   │   ├── seed_database.py        # Load data into Azure SQL
│   │   └── schema.py               # Data schema definitions
│   ├── training/                    # AutoML training
│   │   ├── train_automl.py         # AutoML job submission
│   │   └── config.yaml             # AutoML configuration
│   └── evaluation/                  # Model evaluation
│       └── evaluate_model.py       # Metrics and validation
├── tests/                           # ML tests
├── requirements.txt                 # Python dependencies (pinned)
└── README.md

infra/                               # Terraform Infrastructure
├── main.tf                          # Root module
├── variables.tf                     # Input variables
├── outputs.tf                       # Output values
├── providers.tf                     # AzApi provider 2.8.0 config
└── modules/
    ├── foundry/                     # AI Foundry project + hosted agent + GPT-4o deployment
    ├── ml/                          # Azure ML workspace + endpoint
    ├── sql/                         # Azure SQL Database for appointment data
    ├── static-web-app/              # Azure Static Web Apps
    └── acr/                         # Azure Container Registry

azure.yaml                           # azd deployment manifest
```

**Structure Decision**: Component-root structure selected for independent deployment. Each service (agent, frontend, ml) is a self-contained unit with its own src/, tests/, and README. This enables:
- Clean Dockerfile contexts (`COPY . .` from each root)
- Independent CI/CD pipelines per component
- Isolated tooling (Python venv in ml/, .NET restore in agent/)
- Natural azd service discovery

## Complexity Tracking

> No violations requiring justification. Design follows KISS principle with minimal necessary complexity for the demo requirements.

---

## Constitution Re-Check (Post-Design)

*Final gate validation after Phase 1 design artifacts complete.*

| Principle | Status | Validation |
|-----------|--------|------------|
| **I. Security-First** | ✅ PASS | All contracts use DefaultAzureCredential; no API keys in specs; data-model uses non-PHI synthetic fields |
| **II. Expert Coding Standards** | ✅ PASS | OpenAPI contracts define clean interfaces; data-model follows DDD entity patterns; project structure enforces separation of concerns |
| **III. Clarity Over Cleverness** | ✅ PASS | Quickstart uses step-by-step instructions; API contracts have descriptive names; data-model includes plain-language descriptions |
| **IV. Self-Documenting Code First** | ✅ PASS | All 3 contracts have OpenAPI descriptions; data-model includes validation rules inline; quickstart documents all environment variables |
| **V. Scientific Rigor for Data & ML** | ✅ PASS | research.md documents decision rationale; data-model includes feature engineering justification; ML contract defines probability outputs |

**Post-Design Gate Result**: ✅ PASS - Phase 1 complete. Ready for `/speckit.tasks` to generate implementation tasks.
