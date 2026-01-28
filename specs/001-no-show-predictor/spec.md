# Feature Specification: Medical Appointment No-Show Predictor

**Feature Branch**: `001-no-show-predictor`  
**Created**: 2026-01-28  
**Status**: Draft  
**Input**: User description: "Build a demonstration that includes a machine learning model and chat agent to show how an ML model output combined with Gen AI can surface likely no-show medical appointments and suggest actions."

## Technology Decisions

> **Note**: These are implementation-level decisions captured for context; see Requirements for functional scope.

| Component | Technology | Notes |
|-----------|------------|-------|
| ML Training | Azure AutoML | Automated feature engineering and model selection |
| ML Hosting | Azure ML Managed Online Endpoints | Real-time inference with managed scaling |
| Agent Runtime | Azure AI Foundry Hosted Agents | Containerized agent with conversation state management |
| Agent Framework | Microsoft Agent Framework (.NET) | `Microsoft.Agents.AI` 1.0.0-preview.260127.1 |
| Agent Language | .NET 10 (C#) | Latest preview with hosting adapter |
| Frontend | Blazor WebAssembly | Single-page app with dark/light mode, auto-scroll chat |
| Frontend Hosting | Azure Static Web Apps | Serverless hosting with global CDN |
| Infrastructure | Terraform AzApi Provider 2.8.0 | Native Azure ARM API access |
| Deployment | Azure Developer CLI (azd) | `azd ai agent` extension for hosted agents |
| Authentication | Managed Identity / DefaultAzureCredential | Credential-less auth per constitution |
| Region | North Central US | Required for hosted agents preview |

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Review High-Risk Appointments for Tomorrow (Priority: P1)

As a scheduling coordinator, I want to ask the system which patients are most likely to miss their appointments tomorrow so I can proactively reach out to confirm attendance.

**Why this priority**: This is the core value proposition—identifying no-show risks before they happen enables proactive intervention and reduces revenue loss from empty appointment slots.

**Independent Test**: Can be fully tested by asking "Which patients are at risk of missing their appointments tomorrow?" and receiving a ranked list of appointments with no-show probabilities and patient context.

**Acceptance Scenarios**:

1. **Given** appointments are scheduled for tomorrow, **When** the coordinator asks "Which patients are most likely to miss their appointments tomorrow?", **Then** the system returns a ranked list of appointments sorted by no-show probability with patient name, appointment time, provider, and risk score.

2. **Given** appointments exist with varying risk levels, **When** the coordinator asks "Show me high-risk appointments for tomorrow morning", **Then** the system filters to appointments before noon with no-show probability above a reasonable threshold (e.g., 50%).

3. **Given** no appointments are scheduled for tomorrow, **When** the coordinator asks about tomorrow's no-shows, **Then** the system responds that no appointments are scheduled for that date.

---

### User Story 2 - Get Scheduling Recommendations (Priority: P2)

As a scheduling coordinator, I want the system to recommend specific actions I should take based on no-show predictions so I can efficiently manage my outreach and overbooking strategy.

**Why this priority**: Identifying risk is valuable, but actionable recommendations multiply the value by guiding staff on what to do about the risks.

**Independent Test**: Can be fully tested by asking "What scheduling actions should I take for tomorrow?" and receiving concrete recommendations like confirmation calls, overbooking suggestions, or reminder priorities.

**Acceptance Scenarios**:

1. **Given** high-risk appointments exist for tomorrow, **When** the coordinator asks "What actions should I take to reduce no-shows tomorrow?", **Then** the system provides prioritized recommendations such as "Call these 5 patients to confirm" or "Consider overbooking the 2pm slot with Dr. Smith."

2. **Given** a specific patient has a high no-show probability, **When** the coordinator asks "What should I do about the Johnson appointment?", **Then** the system explains the risk factors and suggests targeted interventions (e.g., "Patient has missed 3 of last 5 appointments—recommend personal phone call and offer transportation assistance if needed").

3. **Given** multiple providers have varying no-show patterns, **When** the coordinator asks "Which providers should I focus overbooking efforts on?", **Then** the system identifies providers with historically higher no-show rates and current high-risk appointment loads.

---

### User Story 3 - Review Weekly No-Show Forecast (Priority: P3)

As a clinic manager, I want to see a summary of predicted no-shows for the upcoming week so I can plan staffing and resource allocation accordingly.

**Why this priority**: Weekly planning enables strategic decisions but is less time-sensitive than daily operational tasks.

**Independent Test**: Can be fully tested by asking "What does the no-show forecast look like for this week?" and receiving a daily breakdown of expected no-show counts and rates.

**Acceptance Scenarios**:

1. **Given** appointments are scheduled for the next 7 days, **When** the manager asks "What's the no-show outlook for this week?", **Then** the system provides a daily summary showing expected no-show count, percentage, and total appointments per day.

2. **Given** certain days have unusually high predicted no-show rates, **When** the manager asks about the weekly forecast, **Then** the system highlights concerning days and explains contributing factors (e.g., "Tuesday shows 35% predicted no-show rate—note: holiday weekend effect").

---

### User Story 4 - Explore Patient No-Show History (Priority: P4)

As a scheduling coordinator, I want to ask about a specific patient's no-show history and risk factors so I can have informed conversations when scheduling or confirming appointments.

**Why this priority**: Supports the other stories by providing drill-down capability, but is not essential for core no-show prevention workflow.

**Independent Test**: Can be fully tested by asking "Tell me about patient Maria Garcia's appointment history" and receiving a summary of past attendance, identified risk factors, and current risk assessment.

**Acceptance Scenarios**:

1. **Given** a patient has appointment history in the system, **When** the coordinator asks "What's the no-show history for patient ID 12345?", **Then** the system returns attendance statistics, identified risk factors, and the patient's current predicted no-show probability.

2. **Given** a patient is new with no history, **When** the coordinator asks about that patient, **Then** the system indicates limited history and explains which demographic or appointment factors contribute to the initial risk assessment.

---

### Edge Cases

- What happens when the ML model service is unavailable? System should gracefully degrade by indicating predictions are temporarily unavailable while still providing historical data.
- How does the system handle appointments with incomplete data (missing patient demographics)? System should still provide predictions using available features and note data quality limitations.
- What happens when a user asks about dates far in the future (e.g., 6 months out)? System should explain that predictions are most reliable within a shorter window and provide appropriate caveats.
- How does the system handle ambiguous date references (e.g., "next Tuesday" when today is Monday)? System should clarify the interpreted date in its response.

## Requirements *(mandatory)*

### Functional Requirements

**Synthetic Data Generation**
- **FR-001**: System MUST generate synthetic patient appointment data following the provided schema with realistic distributions and patterns
- **FR-002**: Synthetic data MUST include historical no-show outcomes to enable model training and validation
- **FR-003**: Data generation MUST produce statistically valid patterns (e.g., higher no-show rates for certain demographics, appointment types, or time patterns based on published research)

**Machine Learning Model**
- **FR-004**: System MUST train a classification model to predict appointment no-show probability
- **FR-005**: Model MUST accept appointment and patient features as input and return a probability score between 0 and 1
- **FR-006**: Model MUST be deployable as a callable service endpoint
- **FR-007**: Model training MUST follow reproducible practices (fixed random seeds, versioned data, logged parameters)
- **FR-008**: Model MUST be validated using proper train/test split methodology to prevent data leakage

**Chat Agent Interface**
- **FR-009**: System MUST provide a conversational interface for querying no-show predictions
- **FR-010**: Agent MUST understand natural language queries about appointments, patients, and time periods
- **FR-011**: Agent MUST orchestrate data retrieval and model inference to answer user questions
- **FR-012**: Agent MUST provide actionable recommendations based on prediction results
- **FR-013**: Agent MUST explain risk factors contributing to high no-show predictions when asked

**Data & Privacy**
- **FR-014**: All patient data MUST be synthetic—no real patient information
- **FR-015**: System MUST NOT store conversation history containing patient identifiers beyond the current session

### Assumptions

- The demonstration uses synthetic data only; no HIPAA compliance is required for this demo
- The chat interface is single-user (no concurrent user session management needed)
- Model retraining is out of scope; the demo uses a pre-trained model
- Integration with real EHR/scheduling systems is out of scope

### Key Entities

- **Patient**: Individual with demographic information (age, gender, insurance status, distance from clinic), contact preferences, and appointment history
- **Appointment**: Scheduled visit with date/time, provider, appointment type, lead time (days scheduled in advance), and historical outcome (kept/no-show)
- **Provider**: Healthcare professional with specialty and schedule
- **Prediction**: Model output associating an appointment with a no-show probability and contributing risk factors
- **Recommendation**: Suggested action based on prediction (confirmation call, overbooking, reminder type)

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can ask about no-show risks and receive accurate, relevant responses within 5 seconds
- **SC-002**: The ML model achieves at least 70% accuracy on held-out test data (demonstrating predictive value beyond random guessing)
- **SC-003**: 90% of natural language queries about appointments, patients, and scheduling actions are correctly interpreted and answered (measured against 20-query evaluation set covering date parsing, patient lookup, provider filtering, and action requests)
- **SC-004**: The system successfully generates at least 5,000 synthetic patient records and 100,000 appointment records spanning 2+ years to capture seasonality patterns
- **SC-005**: Recommendations provided by the agent are actionable and contextually appropriate (verified through sample query testing)
- **SC-006**: Model predictions include explainable risk factors that make clinical sense (e.g., "previous no-show history" as a top factor)
