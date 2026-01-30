# No-Show Predictor Frontend

Blazor WebAssembly chat interface for the Medical Appointment No-Show Predictor.

## Overview

A single-page application providing:
- Chat interface to interact with the no-show prediction agent
- Dark/light theme support
- Auto-scrolling message history
- Loading indicators during agent responses

## Technology Stack

| Component | Technology |
|-----------|------------|
| Framework | Blazor WebAssembly |
| Runtime | .NET 10 |
| UI Library | MudBlazor 7.x |
| Hosting | Azure Static Web Apps |

## Project Structure

```
frontend/
├── src/
│   └── NoShowPredictor.Web/      # Blazor WASM project
│       ├── wwwroot/              # Static assets
│       ├── Pages/
│       │   └── Chat.razor        # Main chat interface
│       ├── Components/
│       │   ├── ChatMessage.razor # Message display component
│       │   └── ThemeToggle.razor # Dark/light mode toggle
│       ├── Services/
│       │   └── AgentApiClient.cs # Foundry agent client
│       ├── Models/
│       │   └── ChatMessage.cs    # Message model
│       └── Program.cs            # WASM entry point
├── tests/
│   └── NoShowPredictor.Web.Tests/
└── README.md
```

## Local Development

### Prerequisites

- .NET 10 SDK
- Modern web browser

### Build & Run

```bash
# Build
dotnet build

# Run with hot reload
dotnet watch run --project src/NoShowPredictor.Web
```

The app will be available at `https://localhost:5001`.

### Environment Variables

Configure in `wwwroot/appsettings.json`:

| Setting | Description |
|---------|-------------|
| `AgentEndpoint` | Azure AI Foundry agent endpoint URL |

## Deployment

Deployed via `azd up` to Azure Static Web Apps.

```bash
# From repository root
azd up
```

## Features

### Chat Interface
- Natural language input for scheduling queries
- Formatted agent responses with risk levels and recommendations
- Message history for current session

### Theme Support
- System preference detection
- Manual dark/light toggle
- Persistent preference storage

## Testing

```bash
dotnet test
```

## Related Documentation

- [Specification](../specs/001-no-show-predictor/spec.md)
- [Implementation Plan](../specs/001-no-show-predictor/plan.md)
- [Agent API Contract](../specs/001-no-show-predictor/contracts/agent-api.openapi.yaml)
