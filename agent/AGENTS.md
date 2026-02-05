# NoShow Predictor Agent - Developer Guide

## Purpose

This agent has ONE focus: **predicting appointment no-show risk**. It does NOT:
- Provide general appointment queries
- Handle scheduling or booking
- Access medical records
- Manage provider schedules

## Quick Start

### Running Locally (Agent + Frontend)

**Terminal 1 - Start the Agent:**
```powershell
cd c:\source\no-show-demo\agent\src\NoShowPredictor.Agent
dotnet run
```
Agent starts on **http://localhost:5100** with AG-UI endpoint at `/ag-ui`

**Terminal 2 - Start the Frontend:**
```powershell
cd c:\source\no-show-demo\frontend\src\NoShowPredictor.Web
dotnet run
```
Frontend starts on **http://localhost:5000**

**Open browser**: Navigate to http://localhost:5000

### Environment Variables (if not using launchSettings.json)

```powershell
$env:AZURE_OPENAI_ENDPOINT = "https://aif-noshow-dev-ncus-001.cognitiveservices.azure.com"
$env:AZURE_OPENAI_DEPLOYMENT_NAME = "gpt-4o"
$env:SQL_CONNECTION_STRING = "Server=sql-noshow-dev-ncus-001.database.windows.net;Database=sqldb-noshow;Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;"
```

## Architecture

### AG-UI Protocol

The agent uses the **AG-UI (Agent GUI) protocol** from Microsoft Agent Framework:

```
┌─────────────────────┐       AG-UI Protocol       ┌─────────────────────┐
│  Blazor WASM        │ ──────────────────────────▶│  .NET Agent Server  │
│  (AGUIChatClient)   │  POST /ag-ui               │  (MapAGUI)          │
│  http://localhost:5000                           │  http://localhost:5100
└─────────────────────┘                            └─────────────────────┘
```

**Server side** (Program.cs):
```csharp
builder.Services.AddAGUI();
// ...
app.MapAGUI("/ag-ui", agent);
```

**Client side** (Program.cs):
```csharp
builder.Services.AddChatClient(sp => 
    new AGUIChatClient(httpClient, "ag-ui"));
```

### Packages

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.Agents.AI` | 1.0.0-preview.260127.1 | Core AI agent types |
| `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` | 1.0.0-preview.260127.1 | AG-UI server endpoint |
| `Microsoft.Agents.AI.AGUI` | 1.0.0-preview.260127.1 | AG-UI client (AGUIChatClient) |
| `Microsoft.Extensions.AI` | 10.2.0 | IChatClient abstraction |

## Tools Available (Only 3)

| Tool | Purpose |
|------|---------|
| `GetNoShowRisk` | Get predictions for which patients may miss appointments on a date |
| `GetSchedulingActions` | Get prioritized actions (calls, overbooking) based on risk |
| `GetPatientRiskProfile` | Look up a specific patient's risk profile |

## Testing the Agent (Command Line)

### Using AG-UI Protocol

The `/ag-ui` endpoint uses Server-Sent Events (SSE) for streaming. For simple testing:

```powershell
# Simple test - the AG-UI endpoint returns SSE stream
$headers = @{ "Accept" = "text/event-stream" }
# Note: This will stream responses - use the frontend for interactive testing
```

### Example Queries (via Frontend)

| Query | Tool Used |
|-------|-----------|
| "What's the no-show risk for tomorrow?" | `GetNoShowRisk` |
| "Which patients are high-risk for next Monday?" | `GetNoShowRisk` |
| "What calls should I make for tomorrow?" | `GetSchedulingActions` |
| "Should I overbook tomorrow?" | `GetSchedulingActions` |
| "What's the risk profile for patient 12345?" | `GetPatientRiskProfile` |

### Out-of-Scope Queries

The agent will politely redirect these:
- "Show me all appointments for Dr. Smith" → "I focus on no-show prediction..."
- "What's the patient's diagnosis?" → "I only analyze attendance patterns..."
- "Book an appointment for..." → "For scheduling, please use your scheduling system..."

## Common Issues

### 1. CORS Errors in Browser
- Agent server includes CORS middleware for local development
- Check browser console for CORS-related errors
- Ensure agent is actually running on port 5100

### 2. Database Connection Errors
- Ensure you're logged in via `az login`
- Connection uses `Authentication=Active Directory Default`
- Check `AppointmentRepository.CreateConnectionAsync()` for auth logic

### 3. Tool Calls Slow
- Large tool responses cause LLM processing delays
- Return summaries/aggregates instead of full objects
- DB queries are fast (~100-200ms), LLM processing is the bottleneck

### 4. Frontend Can't Connect to Agent
- Verify agent is running: `Invoke-RestMethod http://localhost:5100/` (should return 404, not connection refused)
- Check `wwwroot/appsettings.json` has correct `AgentServerUrl`
- Browser console will show connection errors

## Debugging

### View Agent Console Output
The agent logs tool invocations to console.

### Check Active Process
```powershell
Get-Process | Where-Object { $_.ProcessName -like "*NoShowPredictor*" }
```

### Kill Orphaned Process
```powershell
Stop-Process -Name "NoShowPredictor.Agent" -Force -ErrorAction SilentlyContinue
```

### Test Database Connection Directly
```powershell
$tok = az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv
$c = New-Object System.Data.SqlClient.SqlConnection
$c.ConnectionString = "Server=sql-noshow-dev-ncus-001.database.windows.net;Encrypt=True;Database=sqldb-noshow;"
$c.AccessToken = $tok
$c.Open()
$cmd = $c.CreateCommand()
$cmd.CommandText = "SELECT TOP 10 appointmentid FROM appointments"
$r = $cmd.ExecuteReader()
while($r.Read()) { $r[0] }
$c.Close()
```
