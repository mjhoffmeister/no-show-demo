using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;

namespace NoShowPredictor.Agent.Services;

/// <summary>
/// HTTP client for Azure ML managed online endpoint.
/// Uses DefaultAzureCredential for token-based authentication.
/// </summary>
public sealed class MLEndpointClient : IMLEndpointClient
{
    private readonly HttpClient _httpClient;
    private readonly TokenCredential _credential;
    private readonly string _endpointUri;
    private readonly ILogger<MLEndpointClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    // Azure ML scope for token acquisition
    private const string AzureMLScope = "https://ml.azure.com/.default";

    public MLEndpointClient(
        HttpClient httpClient,
        string endpointUri,
        ILogger<MLEndpointClient> logger,
        TokenCredential? credential = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _endpointUri = endpointUri?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(endpointUri));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _credential = credential ?? new DefaultAzureCredential();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task<PredictionResponse> GetPredictionAsync(
        PredictionRequest request,
        CancellationToken cancellationToken = default)
    {
        var accessToken = await GetAccessTokenAsync(cancellationToken);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_endpointUri}/score");
        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        httpRequest.Content = JsonContent.Create(new { input_data = new[] { request } }, options: _jsonOptions);

        _logger.LogDebug("Sending prediction request for appointment {AppointmentId}", request.AppointmentId);

        var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var results = await response.Content.ReadFromJsonAsync<PredictionResponse[]>(_jsonOptions, cancellationToken);

        if (results == null || results.Length == 0)
        {
            throw new InvalidOperationException("ML endpoint returned empty response");
        }

        _logger.LogInformation(
            "Prediction complete for appointment {AppointmentId}: {Probability:P1} ({RiskLevel})",
            request.AppointmentId,
            results[0].NoShowProbability,
            results[0].RiskLevel);

        return results[0];
    }

    public async Task<IReadOnlyList<PredictionResponse>> GetPredictionsBatchAsync(
        IReadOnlyList<PredictionRequest> requests,
        CancellationToken cancellationToken = default)
    {
        if (requests.Count == 0)
        {
            return [];
        }

        var accessToken = await GetAccessTokenAsync(cancellationToken);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_endpointUri}/score");
        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        httpRequest.Content = JsonContent.Create(new { input_data = requests }, options: _jsonOptions);

        _logger.LogDebug("Sending batch prediction request for {Count} appointments", requests.Count);

        var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var results = await response.Content.ReadFromJsonAsync<PredictionResponse[]>(_jsonOptions, cancellationToken);

        if (results == null)
        {
            throw new InvalidOperationException("ML endpoint returned null response");
        }

        _logger.LogInformation("Batch prediction complete for {Count} appointments", results.Length);

        return results;
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var accessToken = await GetAccessTokenAsync(cancellationToken);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"{_endpointUri}/");
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ML endpoint health check failed");
            return false;
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var tokenRequest = new TokenRequestContext([AzureMLScope]);
        var accessToken = await _credential.GetTokenAsync(tokenRequest, cancellationToken);
        return accessToken.Token;
    }
}
