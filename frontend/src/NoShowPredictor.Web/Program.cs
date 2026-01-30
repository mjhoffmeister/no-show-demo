using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using NoShowPredictor.Web;
using NoShowPredictor.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Configure MudBlazor services
builder.Services.AddMudServices();

// Configure HTTP client for agent API
var agentApiBaseUri = builder.Configuration["AgentApiBaseUri"] ?? builder.HostEnvironment.BaseAddress;
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(agentApiBaseUri) });

// Register Agent API client
builder.Services.AddScoped<IAgentApiClient, AgentApiClient>();

await builder.Build().RunAsync();
