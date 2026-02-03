using Azure.AI.AgentServer.AgentFramework.Extensions;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

// Get configuration from environment variables
var openAiEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
    ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o";

var credential = new DefaultAzureCredential();

// Create chat client
var chatClient = new AzureOpenAIClient(new Uri(openAiEndpoint), credential)
    .GetChatClient(deploymentName)
    .AsIChatClient()
    .AsBuilder()
    .UseOpenTelemetry(sourceName: "NoShowAgent", configure: cfg => cfg.EnableSensitiveData = true)
    .Build();

// Create the agent with instructions
var agent = new ChatClientAgent(
    chatClient,
    name: "NoShowPredictor",
    instructions: """
        You are a medical appointment no-show prediction assistant. You help healthcare staff 
        identify patients at risk of missing their appointments and suggest appropriate interventions.

        When asked about no-show predictions or appointment risks:
        1. Query the appointments database to get relevant appointment data
        2. Use the ML prediction endpoint to calculate no-show probabilities
        3. Provide clear recommendations based on risk levels

        Risk levels and recommended actions:
        - High risk (>70%): Recommend phone call reminder and backup scheduling
        - Medium risk (40-70%): Recommend SMS reminder and follow-up
        - Low risk (<40%): Standard reminder is sufficient

        Always be professional and HIPAA-compliant in your responses.
        Do not share patient identifiable information unnecessarily.
        """)
    .AsBuilder()
    .UseOpenTelemetry(sourceName: "NoShowAgent", configure: cfg => cfg.EnableSensitiveData = true)
    .Build();

// Run agent
await agent.RunAIAgentAsync(telemetrySourceName: "NoShowAgent");
