// Suppress experimental API warning for GetResponsesClient
#pragma warning disable OPENAI001

using Azure.AI.AgentServer.AgentFramework.Extensions;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using NoShowPredictor.Agent;

// Get configuration from environment
string openAiEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
    ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is required.");
string deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME")
    ?? "gpt-4o";
string sqlConnectionString = Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING")
    ?? "Server=sql-noshow-dev-ncus-001.database.windows.net;Database=sqldb-noshow;Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;";
string mlEndpointUri = Environment.GetEnvironmentVariable("ML_ENDPOINT_URI")
    ?? "https://noshow-predictor.northcentralus.inference.ml.azure.com";

Console.WriteLine($"Deployment: {deploymentName}");
Console.WriteLine($"OpenAI Endpoint: {openAiEndpoint}");
Console.WriteLine($"SQL Connection: {sqlConnectionString}");
Console.WriteLine($"ML Endpoint: {mlEndpointUri}");

// Create the NoShowAgent with tools
NoShowAgent noShowAgent = NoShowAgent.Create(sqlConnectionString, mlEndpointUri);
List<AITool> tools = noShowAgent.GetTools().Cast<AITool>().ToList();

Console.WriteLine($"Loaded {tools.Count} tools for NoShowAgent:");
foreach (AITool tool in tools)
{
    Console.WriteLine($"  - {tool.Name}: {tool.Description?.Substring(0, Math.Min(60, tool.Description?.Length ?? 0))}...");
}

// Create an IChatClient from Azure OpenAI
IChatClient chatClient = new AzureOpenAIClient(
    new Uri(openAiEndpoint),
    new DefaultAzureCredential())
    .GetChatClient(deploymentName)
    .AsIChatClient();

// Create the AI agent using the ChatClient
AIAgent agent = chatClient.CreateAIAgent(
    name: "NoShowPredictor",
    instructions: NoShowAgent.SystemPrompt,
    description: "Healthcare appointment no-show predictor agent",
    tools: tools);

Console.WriteLine("NoShowPredictor Agent starting with Responses API...");

// RunAIAgentAsync hosts the agent with /responses endpoint on port 8088
await agent.RunAIAgentAsync(telemetrySourceName: "NoShowPredictor");

