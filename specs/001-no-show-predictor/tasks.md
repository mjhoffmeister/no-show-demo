# Tasks: Medical Appointment No-Show Predictor

**Input**: Design documents from `/specs/001-no-show-predictor/`  
**Generated**: 2026-01-28  
**User Stories**: 4 (P1-P4 from spec.md)

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: User story label (US1, US2, US3, US4)
- All paths are relative to repository root

---

## Phase 1: Setup

**Purpose**: Project initialization, directory structure, and base configurations

- [x] T001 Create directory structure: agent/, frontend/, ml/, infra/ per plan.md
- [x] T002 [P] Initialize .NET solution with agent/NoShowPredictor.sln
- [x] T003 [P] Initialize frontend/src/NoShowPredictor.Web Blazor WASM project with .NET 10
- [x] T004 [P] Initialize ml/ Python project with requirements.txt per research.md Section 2.1 (azure-ai-ml==1.31.0, pandas==3.0.0, pyarrow==23.0.0, faker==40.1.2, etc.)
- [x] T005 [P] Create infra/providers.tf with AzApi provider 2.8.0 configuration
- [x] T006 [P] Create azure.yaml azd manifest with agent, frontend, ml services
- [x] T007 [P] Create .env.example with AZURE_SUBSCRIPTION_ID, AZURE_LOCATION=northcentralus
- [x] T008 [P] Add .gitignore for .NET, Python, Terraform artifacts
- [x] T009 [P] Create agent/README.md with component overview
- [x] T010 [P] Create frontend/README.md with component overview
- [x] T011 [P] Create ml/README.md with component overview

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Infrastructure, data generation, and ML model that ALL user stories depend on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

### Infrastructure (Terraform)

> **Note**: All resource names MUST follow constitution Infrastructure Standards (CAF naming convention). See `.specify/memory/constitution.md` for required prefixes.

- [x] T012 [P] Create infra/variables.tf with subscription_id, location, resource_prefix variables
- [x] T013 [P] Create infra/main.tf root module importing all child modules
- [x] T014 [P] Create infra/outputs.tf exposing endpoints and connection strings
- [x] T015 [P] Create infra/modules/foundry/main.tf with AI Foundry account and project
- [x] T016 [P] Create infra/modules/foundry/gpt-deployment.tf with GPT-4o model deployment
- [x] T017 [P] Create infra/modules/ml/main.tf with Azure ML workspace and compute cluster
- [x] T018 [P] Create infra/modules/ml/endpoint.tf with managed online endpoint resource
- [x] T019 [P] Create infra/modules/sql/main.tf with Azure SQL Database (Basic tier)
- [x] T020 [P] Create infra/modules/sql/schema.sql with tables for Patient, Provider, Department, Appointment, Insurance
- [x] T021 [P] Create infra/modules/acr/main.tf with Azure Container Registry
- [x] T022 [P] Create infra/modules/static-web-app/main.tf with Azure Static Web Apps
- [x] T023 Create infra/modules/foundry/rbac.tf with Managed Identity role assignments (depends on T015-T022)

### Synthetic Data Generation (Python)

- [x] T024 Create ml/src/data/schema.py with dataclasses for Patient, Provider, Department, Appointment, Insurance per data-model.md
- [x] T025 Create ml/src/data/generate_synthetic.py with 5000 patients, 100 providers, 40 departments, 100000 appointments spanning 24+ months with patient journey patterns
- [x] T026 Create ml/src/data/seed_database.py to load parquet files into Azure SQL Database
- [x] T027 Add ml/tests/test_synthetic_data.py to validate data distributions, no-show rate ~22%, seasonality patterns, and patient journey sequences

### ML Model Training (Python)

- [x] T028 Create ml/src/training/config.yaml with AutoML classification settings per research.md
- [x] T029 Create ml/src/training/train_automl.py to submit AutoML job with experiment tracking
- [x] T030 Create ml/src/evaluation/evaluate_model.py to compute accuracy, AUC, feature importance
- [x] T031 Create ml/deployment/endpoint.yaml with managed endpoint configuration
- [x] T032 Create ml/deployment/deployment.yaml with model deployment settings
- [x] T033 Create ml/src/evaluation/test_endpoint.py to validate deployed endpoint returns predictions

### Agent Core Components (.NET)

- [x] T034 Create agent/src/NoShowPredictor.Agent/NoShowPredictor.Agent.csproj with Microsoft.Agents.AI 1.0.0-preview.260127.1, Azure.AI.Projects 1.1.0, Azure.Identity, Microsoft.Data.SqlClient
- [x] T035 Create agent/src/NoShowPredictor.Agent/Models/Patient.cs entity per data-model.md
- [x] T036 [P] Create agent/src/NoShowPredictor.Agent/Models/Provider.cs entity per data-model.md
- [x] T037 [P] Create agent/src/NoShowPredictor.Agent/Models/Department.cs entity per data-model.md
- [x] T038 [P] Create agent/src/NoShowPredictor.Agent/Models/Appointment.cs entity per data-model.md
- [x] T039 [P] Create agent/src/NoShowPredictor.Agent/Models/Insurance.cs entity per data-model.md
- [x] T040 [P] Create agent/src/NoShowPredictor.Agent/Models/Prediction.cs with RiskFactor embedded type
- [x] T041 [P] Create agent/src/NoShowPredictor.Agent/Models/Recommendation.cs entity
- [x] T042 Create agent/src/NoShowPredictor.Agent/Data/AppointmentRepository.cs with data access layer using Managed Identity connection
- [x] T043 Create agent/src/NoShowPredictor.Agent/Services/IMLEndpointClient.cs interface per ml-inference.openapi.yaml
- [x] T044 Create agent/src/NoShowPredictor.Agent/Services/MLEndpointClient.cs implementation using DefaultAzureCredential
- [x] T045 Create agent/src/NoShowPredictor.Agent/Program.cs with hosting adapter, DI registration, and telemetry
- [x] T046 Create agent/Dockerfile with .NET 10 runtime image

### Frontend Core Components (Blazor)

- [x] T047 Create frontend/src/NoShowPredictor.Web/NoShowPredictor.Web.csproj with MudBlazor 7.x
- [x] T048 Create frontend/src/NoShowPredictor.Web/Program.cs with MudBlazor and HttpClient registration
- [x] T049 Create frontend/src/NoShowPredictor.Web/wwwroot/index.html with MudBlazor CSS/JS references
- [x] T050 Create frontend/src/NoShowPredictor.Web/Shared/MainLayout.razor with MudThemeProvider
- [x] T051 Create frontend/src/NoShowPredictor.Web/Services/AgentApiClient.cs per agent-api.openapi.yaml

**Checkpoint**: Infrastructure deployed, data seeded, ML endpoint live, agent/frontend scaffolded

---

## Phase 3: User Story 1 - Review High-Risk Appointments Tomorrow (Priority: P1) 🎯 MVP

**Goal**: Scheduling coordinator asks about tomorrow's high-risk appointments and gets a ranked list with probabilities

**Independent Test**: Ask "Which patients are at risk of missing their appointments tomorrow?" → receive ranked list with patient name, time, provider, risk score

### Agent Implementation

- [x] T052 [US1] Create agent/src/NoShowPredictor.Agent/Tools/AppointmentTool.cs with GetAppointmentsByDateRange method
- [x] T053 [US1] Create agent/src/NoShowPredictor.Agent/Tools/PredictionTool.cs with GetPredictions method calling ML endpoint
- [x] T054 [US1] Create agent/src/NoShowPredictor.Agent/NoShowAgent.cs with system prompt from research.md and tools registration
- [x] T055 [US1] Add date parsing logic to AppointmentTool for "tomorrow", "today", relative dates
- [x] T056 [US1] Add risk level filtering (High/Medium/Low) to GetAppointmentsByDateRange
- [x] T057 [US1] Update Program.cs to wire NoShowAgent with ChatClientAgent and RunAIAgentAsync

### Frontend Implementation

- [x] T058 [US1] Create frontend/src/NoShowPredictor.Web/Models/ChatMessage.cs with Role, Content, Timestamp
- [x] T059 [US1] Create frontend/src/NoShowPredictor.Web/Components/ChatMessage.razor with user/assistant styling
- [x] T060 [US1] Create frontend/src/NoShowPredictor.Web/Pages/Chat.razor with message list and input box
- [x] T061 [US1] Add auto-scroll to latest message in Chat.razor
- [x] T062 [US1] Add loading indicator during agent response in Chat.razor
- [x] T063 [US1] Wire Chat.razor to AgentApiClient.CreateResponseAsync

### Integration

- [x] T064 [US1] Create agent/agent.yaml azd agent manifest with container config, environment variables, and session-only conversation state (FR-015: no persistent storage of patient identifiers)
- [x] T065 [US1] Update azure.yaml to include agent service with ACR build
- [ ] T066 [US1] Test end-to-end: deploy with `azd up`, ask about tomorrow's appointments

**Checkpoint**: MVP complete - users can ask about high-risk appointments for tomorrow

---

## Phase 4: User Story 2 - Get Scheduling Recommendations (Priority: P2)

**Goal**: System provides actionable recommendations (confirmation calls, overbooking) based on predictions

**Independent Test**: Ask "What scheduling actions should I take for tomorrow?" → receive prioritized recommendations

### Agent Implementation

- [ ] T067 [US2] Create agent/src/NoShowPredictor.Agent/Services/RecommendationService.cs with GenerateRecommendations logic
- [ ] T068 [US2] Add recommendation rules: High risk + morning → ConfirmationCall, High risk + provider history → Overbook
- [ ] T069 [US2] Create agent/src/NoShowPredictor.Agent/Tools/RecommendationTool.cs with GetRecommendations method
- [ ] T070 [US2] Update NoShowAgent.cs to register RecommendationTool
- [ ] T071 [US2] Add provider-level aggregation to AppointmentTool for overbooking analysis
- [ ] T072 [US2] Add patient-specific recommendation lookup for "What should I do about [patient] appointment?"

### Frontend Enhancement

- [ ] T073 [US2] Add recommendation styling in ChatMessage.razor (priority badges, action icons)
- [ ] T074 [US2] Add clickable patient names in chat to drill down (prepares for US4)

**Checkpoint**: System provides actionable scheduling recommendations

---

## Phase 5: User Story 3 - Review Weekly Forecast (Priority: P3)

**Goal**: Clinic manager sees weekly summary with daily no-show predictions

**Independent Test**: Ask "What does the no-show forecast look like for this week?" → receive daily breakdown

### Agent Implementation

- [ ] T075 [US3] Add GetWeeklyForecast method to AppointmentTool returning 7-day aggregation
- [ ] T076 [US3] Create aggregation logic: total appointments, predicted no-shows, percentage per day
- [ ] T077 [US3] Add anomaly detection for days with significantly higher predicted rates
- [ ] T078 [US3] Add contributing factor explanation for high-risk days (e.g., "Monday after holiday")

### Frontend Enhancement

- [ ] T079 [US3] Add simple table/list formatting for weekly data in ChatMessage.razor

**Checkpoint**: Weekly forecast with daily breakdown available

---

## Phase 6: User Story 4 - Explore Patient History (Priority: P4)

**Goal**: Drill down into specific patient's appointment history and risk factors

**Independent Test**: Ask "Tell me about patient Maria Garcia's appointment history" → receive attendance stats, risk factors

### Agent Implementation

- [ ] T080 [US4] Create agent/src/NoShowPredictor.Agent/Tools/PatientTool.cs with GetPatientHistory method
- [ ] T081 [US4] Add historical_no_show_rate and historical_no_show_count calculation
- [ ] T082 [US4] Add risk factor explanation (insurance type, lead time patterns, day preferences)
- [ ] T083 [US4] Update NoShowAgent.cs to register PatientTool
- [ ] T084 [US4] Handle "new patient" case with limited history explanation

**Checkpoint**: Patient history drill-down complete

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Error handling, edge cases, documentation, deployment validation

- [ ] T085 [P] Add graceful degradation when ML endpoint unavailable in PredictionTool.cs
- [ ] T086 [P] Add data quality warnings for appointments with missing demographics in AppointmentTool.cs
- [ ] T087 [P] Add date range validation (warn for predictions >2 weeks out) in AppointmentTool.cs
- [ ] T088 [P] Add ambiguous date clarification in agent responses
- [ ] T089 Create frontend/src/NoShowPredictor.Web/Components/ThemeToggle.razor for dark/light mode
- [ ] T090 Add ThemeToggle to MainLayout.razor header
- [ ] T091 [P] Add agent/tests/NoShowPredictor.Agent.Tests/Tools/AppointmentToolTests.cs
- [ ] T092 [P] Add agent/tests/NoShowPredictor.Agent.Tests/Tools/PredictionToolTests.cs
- [ ] T093 [P] Add agent/tests/NoShowPredictor.Agent.Tests/Services/MLEndpointClientTests.cs
- [ ] T094 Run specs/001-no-show-predictor/quickstart.md end-to-end validation
- [ ] T095 Update root README.md with project overview and quickstart link

---

## Dependencies & Execution Order

### Phase Dependencies

```
Phase 1: Setup ─────────────────────────────────────┐
                                                     │
Phase 2: Foundational ◄──────────────────────────────┘
    │
    ├── Infrastructure (T012-T023)
    ├── Synthetic Data (T024-T027)
    ├── ML Model (T028-T033)
    ├── Agent Core (T034-T046)
    └── Frontend Core (T047-T051)
    │
    ▼ ─────────── BLOCKS ALL USER STORIES ───────────
    │
    ├─► Phase 3: User Story 1 (P1) MVP ◄─────────────┐
    │       Can start after Foundational             │
    │                                                │
    ├─► Phase 4: User Story 2 (P2) ◄─────────────────┤ Can run in
    │       Can start after Foundational             │ parallel if
    │                                                │ team capacity
    ├─► Phase 5: User Story 3 (P3) ◄─────────────────┤ allows
    │       Can start after Foundational             │
    │                                                │
    └─► Phase 6: User Story 4 (P4) ◄─────────────────┘
            Can start after Foundational
    │
    ▼
Phase 7: Polish
    Depends on desired user stories complete
```

### Within Phase 2 (Foundational)

- Infrastructure tasks (T012-T023) can run in parallel, except T023 (RBAC) depends on all resources
- Synthetic Data (T024-T027) can run in parallel with Infrastructure
- ML Model (T028-T033) depends on Infrastructure (needs ML workspace)
- Agent Core (T034-T046) depends on Infrastructure (needs SQL Database)
- Frontend Core (T047-T051) can run in parallel with Agent Core

### Within User Stories

Each story:
1. Agent tools/services first
2. Frontend enhancements second
3. Integration/testing last

---

## Parallel Execution Examples

### Phase 1 Setup (All Parallel)

```bash
# All T002-T011 can run simultaneously
T002: Initialize .NET solution
T003: Initialize Blazor project
T004: Initialize Python project
T005: Create Terraform providers
T006-T011: Create manifests and READMEs
```

### Phase 2 Infrastructure (All Parallel)

```bash
# Launch all Terraform modules together
T012-T022: All module files can be created in parallel
# Then T023 after all modules exist
```

### Phase 2 Models (All Parallel)

```bash
# All C# entity models can be created simultaneously
T035: Patient.cs
T036: Provider.cs
T037: Department.cs
T038: Appointment.cs
T039: Insurance.cs
T040: Prediction.cs
T041: Recommendation.cs
```

### User Stories (Parallel if Team Allows)

```bash
# After Phase 2 completes, different developers can work on:
Developer A: Phase 3 (US1 - MVP)
Developer B: Phase 4 (US2 - Recommendations)
Developer C: Phase 5 (US3 - Weekly Forecast)
```

---

## Implementation Strategy

### MVP First (Recommended)

1. Complete Phase 1: Setup (~30 min)
2. Complete Phase 2: Foundational (~4 hours)
3. Complete Phase 3: User Story 1 (~2 hours)
4. **STOP and VALIDATE**: Test with "Which patients are at risk tomorrow?"
5. Deploy/demo MVP

### Incremental Delivery

| Increment | Tasks | Value Delivered |
|-----------|-------|-----------------|
| MVP | T001-T066 | Ask about tomorrow's high-risk appointments |
| +Recommendations | T067-T074 | Get actionable scheduling advice |
| +Weekly | T075-T079 | Plan the week ahead |
| +History | T080-T084 | Drill down on specific patients |
| Polish | T085-T095 | Production-ready with edge cases |

---

## Summary

| Metric | Count |
|--------|-------|
| **Total Tasks** | 95 |
| **Phase 1 (Setup)** | 11 |
| **Phase 2 (Foundational)** | 40 |
| **Phase 3 (US1 - MVP)** | 15 |
| **Phase 4 (US2)** | 8 |
| **Phase 5 (US3)** | 5 |
| **Phase 6 (US4)** | 5 |
| **Phase 7 (Polish)** | 11 |
| **Parallelizable [P]** | 45 |
| **MVP Scope** | 66 tasks (T001-T066) |
