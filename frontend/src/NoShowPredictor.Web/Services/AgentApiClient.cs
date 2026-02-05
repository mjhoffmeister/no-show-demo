using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace NoShowPredictor.Web.Services;

/// <summary>
/// Interface for Agent API client operations.
/// Based on agent-api.openapi.yaml contract.
/// </summary>
public interface IAgentApiClient
{
    /// <summary>Get appointments with optional filters.</summary>
    Task<AppointmentListResponse> GetAppointmentsAsync(
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        string? riskLevel = null,
        int? providerId = null,
        int? departmentId = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    /// <summary>Get high-risk appointments for a date range.</summary>
    Task<IReadOnlyList<AppointmentDto>> GetHighRiskAppointmentsAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);

    /// <summary>Get appointment details with prediction.</summary>
    Task<AppointmentDetailDto?> GetAppointmentAsync(int appointmentId, CancellationToken cancellationToken = default);

    /// <summary>Get prediction for an appointment.</summary>
    Task<PredictionDto?> GetPredictionAsync(int appointmentId, CancellationToken cancellationToken = default);

    /// <summary>Send a chat message to the agent.</summary>
    Task<ChatResponse> SendChatMessageAsync(ChatRequest request, CancellationToken cancellationToken = default);

    /// <summary>Get chat conversation history.</summary>
    Task<IReadOnlyList<ChatMessage>> GetChatHistoryAsync(string conversationId, CancellationToken cancellationToken = default);

    /// <summary>Check API health.</summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Agent API client using Azure.AI.Projects SDK.
/// </summary>
public sealed class AgentApiClient : IAgentApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IAccessTokenProvider _tokenProvider;
    private readonly string _projectEndpoint;
    private const string AgentName = "noshow-predictor";

    public AgentApiClient(HttpClient httpClient, IAccessTokenProvider tokenProvider, AgentApiOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        
        // Extract project endpoint from full agent URL if needed
        // AgentServerUrl might be: https://...services.ai.azure.com/api/projects/{project}/agents/{agent}
        // We need just: https://...services.ai.azure.com/api/projects/{project}
        var fullEndpoint = options?.ProjectEndpoint ?? throw new ArgumentNullException(nameof(options));
        var agentsSuffix = $"/agents/{AgentName}";
        if (fullEndpoint.EndsWith(agentsSuffix, StringComparison.OrdinalIgnoreCase))
        {
            _projectEndpoint = fullEndpoint[..^agentsSuffix.Length];
        }
        else
        {
            _projectEndpoint = fullEndpoint;
        }
    }

    public async Task<AppointmentListResponse> GetAppointmentsAsync(
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        string? riskLevel = null,
        int? providerId = null,
        int? departmentId = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var queryParams = new List<string>();
        if (startDate.HasValue) queryParams.Add($"startDate={startDate:yyyy-MM-dd}");
        if (endDate.HasValue) queryParams.Add($"endDate={endDate:yyyy-MM-dd}");
        if (!string.IsNullOrEmpty(riskLevel)) queryParams.Add($"riskLevel={Uri.EscapeDataString(riskLevel)}");
        if (providerId.HasValue) queryParams.Add($"providerId={providerId}");
        if (departmentId.HasValue) queryParams.Add($"departmentId={departmentId}");
        queryParams.Add($"page={page}");
        queryParams.Add($"pageSize={pageSize}");

        var url = $"api/appointments?{string.Join("&", queryParams)}";
        var response = await _httpClient.GetFromJsonAsync<AppointmentListResponse>(url, cancellationToken);
        return response ?? new AppointmentListResponse { Items = [], TotalCount = 0, Page = page, PageSize = pageSize };
    }

    public async Task<IReadOnlyList<AppointmentDto>> GetHighRiskAppointmentsAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/appointments/high-risk?startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}";
        var response = await _httpClient.GetFromJsonAsync<AppointmentDto[]>(url, cancellationToken);
        return response ?? [];
    }

    public async Task<AppointmentDetailDto?> GetAppointmentAsync(int appointmentId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<AppointmentDetailDto>($"api/appointments/{appointmentId}", cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<PredictionDto?> GetPredictionAsync(int appointmentId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<PredictionDto>($"api/appointments/{appointmentId}/prediction", cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<ChatResponse> SendChatMessageAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        // Use Azure.AI.Projects SDK with MSAL token credential
        var credential = new MsalTokenCredential(_tokenProvider, ["https://ai.azure.com/.default"]);
        var projectClient = new AIProjectClient(new Uri(_projectEndpoint), credential);

        // Get the agent
        var agentResult = await projectClient.Agents.GetAgentAsync(AgentName, cancellationToken);
        var agentRecord = agentResult.Value;
        
        // Get response client configured for this agent
        var responseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(agentRecord);

        // Send message and get response
        var response = await responseClient.CreateResponseAsync(request.Message, cancellationToken: cancellationToken);

        return new ChatResponse
        {
            ConversationId = response.Value.Id ?? Guid.NewGuid().ToString(),
            Response = response.Value.GetOutputText() ?? "No response received"
        };
    }

    // Agent Server Framework request/response DTOs - kept for reference but no longer used with SDK

    public async Task<IReadOnlyList<ChatMessage>> GetChatHistoryAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync<ChatMessage[]>($"api/chat/{conversationId}/history", cancellationToken);
        return response ?? [];
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Use SDK to check if we can get the agent
            var credential = new MsalTokenCredential(_tokenProvider, ["https://ai.azure.com/.default"]);
            var projectClient = new AIProjectClient(new Uri(_projectEndpoint), credential);
            var agent = await projectClient.Agents.GetAgentAsync(AgentName, cancellationToken);
            return agent != null;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Configuration options for Agent API client.
/// </summary>
public sealed class AgentApiOptions
{
    public required string ProjectEndpoint { get; init; }
}

#region DTOs

public sealed record AppointmentListResponse
{
    public required IReadOnlyList<AppointmentDto> Items { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
}

public record AppointmentDto
{
    public required int AppointmentId { get; init; }
    public required DateOnly AppointmentDate { get; init; }
    public required string AppointmentStartTime { get; init; }
    public required string AppointmentTypeName { get; init; }
    public required string AppointmentStatus { get; init; }
    public required string PatientAgeBucket { get; init; }
    public required string PatientGender { get; init; }
    public required string ProviderName { get; init; }
    public required string DepartmentName { get; init; }
    public string? RiskLevel { get; init; }
}

public sealed record AppointmentDetailDto : AppointmentDto
{
    public required int PatientId { get; init; }
    public required int ProviderId { get; init; }
    public required int DepartmentId { get; init; }
    public required int AppointmentDuration { get; init; }
    public required string VirtualFlag { get; init; }
    public required string NewPatientFlag { get; init; }
    public int HistoricalNoShowCount { get; init; }
    public int HistoricalApptCount { get; init; }
    public double? DistanceFromClinicMiles { get; init; }
    public bool HasActivePortal { get; init; }
    public PredictionDto? Prediction { get; init; }
    public IReadOnlyList<RecommendationDto>? Recommendations { get; init; }
}

public sealed record PredictionDto
{
    public required Guid PredictionId { get; init; }
    public required int AppointmentId { get; init; }
    public required string RiskLevel { get; init; }
    public required IReadOnlyList<RiskFactorDto> RiskFactors { get; init; }
    public required string ModelVersion { get; init; }
    public required DateTime PredictedAt { get; init; }
}

public sealed record RiskFactorDto
{
    public required string Name { get; init; }
    public required double Contribution { get; init; }
    public required string Description { get; init; }
}

public sealed record RecommendationDto
{
    public required int RecommendationId { get; init; }
    public required string ActionType { get; init; }
    public required int Priority { get; init; }
    public required string Description { get; init; }
    public required string Rationale { get; init; }
    public bool? ActionTaken { get; init; }
}

public sealed record ChatRequest
{
    public required string Message { get; init; }
    public string? ConversationId { get; init; }
    public int? AppointmentId { get; init; }
}

public sealed record ChatResponse
{
    public required string ConversationId { get; init; }
    public required string Response { get; init; }
    public IReadOnlyList<AppointmentDto>? ReferencedAppointments { get; init; }
    public IReadOnlyList<RecommendationDto>? SuggestedActions { get; init; }
}

public sealed record ChatMessage
{
    public required string Role { get; init; } // "user" or "assistant"
    public required string Content { get; init; }
    public required DateTime Timestamp { get; init; }
}

#endregion