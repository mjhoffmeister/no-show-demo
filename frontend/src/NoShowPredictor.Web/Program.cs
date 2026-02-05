using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using NoShowPredictor.Web;
using NoShowPredictor.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Configure MudBlazor services
builder.Services.AddMudServices();

builder.Services.AddMsalAuthentication(options =>
{
	builder.Configuration.Bind("AzureAd", options.ProviderOptions.Authentication);
	options.ProviderOptions.DefaultAccessTokenScopes.Add("https://ai.azure.com/.default");
});

// Configure Agent API client with Azure.AI.Projects SDK
var agentServerUrl = (builder.Configuration["AgentServerUrl"] ?? "http://localhost:8088").TrimEnd('/');

// Register options
builder.Services.AddSingleton(new AgentApiOptions { ProjectEndpoint = agentServerUrl });

// Register HttpClient for other API calls (appointments, etc.)
builder.Services.AddHttpClient<IAgentApiClient, AgentApiClient>(client =>
{
    client.BaseAddress = new Uri(agentServerUrl.EndsWith('/') ? agentServerUrl : agentServerUrl + "/");
})
.AddHttpMessageHandler(sp =>
    sp.GetRequiredService<AuthorizationMessageHandler>()
      .ConfigureHandler(authorizedUrls: [agentServerUrl], scopes: ["https://ai.azure.com/.default"])
);

await builder.Build().RunAsync();
