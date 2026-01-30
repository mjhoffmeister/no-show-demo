using Azure.AI.Inference;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.Extensions.AI;
using NoShowPredictor.Agent.Data;
using NoShowPredictor.Agent.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure Azure Monitor/Application Insights telemetry
if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}

// Configure HTTP client factory
builder.Services.AddHttpClient();

// Configure Azure credential (credential-less auth)
builder.Services.AddSingleton<DefaultAzureCredential>(_ => new DefaultAzureCredential());

// Configure SQL connection string with Managed Identity
var sqlServer = builder.Configuration["SQL_SERVER"] ?? throw new InvalidOperationException("SQL_SERVER not configured");
var sqlDatabase = builder.Configuration["SQL_DATABASE"] ?? throw new InvalidOperationException("SQL_DATABASE not configured");
var sqlConnectionString = $"Server=tcp:{sqlServer},1433;Database={sqlDatabase};Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;";

// Register AppointmentRepository
builder.Services.AddSingleton<IAppointmentRepository>(sp =>
    new AppointmentRepository(
        sqlConnectionString,
        sp.GetRequiredService<ILogger<AppointmentRepository>>()));

// Configure ML endpoint client
var mlEndpointUri = builder.Configuration["ML_ENDPOINT_URI"] ?? throw new InvalidOperationException("ML_ENDPOINT_URI not configured");
builder.Services.AddSingleton<IMLEndpointClient>(sp =>
    new MLEndpointClient(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("MLEndpoint"),
        mlEndpointUri,
        sp.GetRequiredService<ILogger<MLEndpointClient>>(),
        sp.GetRequiredService<DefaultAzureCredential>()));

// Configure Azure AI Foundry chat client for the agent
var aiFoundryEndpoint = builder.Configuration["AI_FOUNDRY_ENDPOINT"] ?? throw new InvalidOperationException("AI_FOUNDRY_ENDPOINT not configured");
var aiFoundryDeployment = builder.Configuration["AI_FOUNDRY_DEPLOYMENT"] ?? "gpt-4o";

// Register the base IChatClient with function invocation support
builder.Services.AddSingleton<IChatClient>(sp =>
{
    // Create the underlying Azure AI Foundry chat client
    var credential = sp.GetRequiredService<DefaultAzureCredential>();
    var chatCompletionsClient = new ChatCompletionsClient(
        new Uri(aiFoundryEndpoint),
        credential);
    var innerChatClient = chatCompletionsClient.AsIChatClient(aiFoundryDeployment);
    
    // Wrap with function invocation middleware for automatic tool calling
    return new ChatClientBuilder(innerChatClient)
        .UseFunctionInvocation()
        .UseOpenTelemetry(sourceName: "NoShowAgent")
        .Build();
});

// Register the Agent Chat Service (wraps IChatClient with tools and system prompt)
builder.Services.AddSingleton<IAgentChatService, AgentChatService>();

// Configure CORS for Blazor WebAssembly client
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add controllers for REST API
builder.Services.AddControllers();

// Add health checks
builder.Services.AddHealthChecks()
    .AddCheck("ml-endpoint", () =>
    {
        // Placeholder - actual implementation would check ML endpoint
        return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy();
    });

// Configure OpenAPI/Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "No-Show Predictor Agent API",
        Version = "v1",
        Description = "Azure AI Foundry Hosted Agent API for medical appointment no-show predictions"
    });
});

var app = builder.Build();

// Configure middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthorization();

// Map controllers
app.MapControllers();

// Map health check endpoint
app.MapHealthChecks("/health");

// Root endpoint
app.MapGet("/", () => Results.Ok(new
{
    service = "NoShowPredictor.Agent",
    version = "1.0.0",
    status = "running"
}));

app.Run();
