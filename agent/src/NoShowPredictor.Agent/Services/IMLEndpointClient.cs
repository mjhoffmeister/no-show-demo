using NoShowPredictor.Agent.Models;

namespace NoShowPredictor.Agent.Services;

/// <summary>
/// Request model for ML inference endpoint.
/// Matches ml-inference.openapi.yaml schema.
/// </summary>
public sealed record PredictionRequest
{
    public required int AppointmentId { get; init; }
    public required string PatientAgeBucket { get; init; }
    public required string PatientGender { get; init; }
    public required double DistanceFromClinicMiles { get; init; }
    public required bool HasActivePortal { get; init; }
    public required int HistoricalNoShowCount { get; init; }
    public required int HistoricalApptCount { get; init; }
    public required int LeadTimeDays { get; init; }
    public required int DayOfWeek { get; init; }
    public required int HourOfDay { get; init; }
    public required string AppointmentTypeName { get; init; }
    public required string ProviderSpecialty { get; init; }
    public required string DepartmentSpecialty { get; init; }
    public required string VirtualFlag { get; init; }
    public required string NewPatientFlag { get; init; }
    public required string PayerGrouping { get; init; }
}

/// <summary>
/// Response model from ML inference endpoint.
/// Matches ml-inference.openapi.yaml schema.
/// </summary>
public sealed record PredictionResponse
{
    public required int AppointmentId { get; init; }
    public required double NoShowProbability { get; init; }
    public required string RiskLevel { get; init; }
    public required IReadOnlyList<RiskFactorResponse> RiskFactors { get; init; }
    public required string ModelVersion { get; init; }
}

/// <summary>
/// Risk factor in response.
/// </summary>
public sealed record RiskFactorResponse
{
    public required string Name { get; init; }
    public required double Contribution { get; init; }
    public required string Description { get; init; }
}

/// <summary>
/// Interface for ML inference endpoint client.
/// </summary>
public interface IMLEndpointClient
{
    /// <summary>Get prediction for a single appointment.</summary>
    Task<PredictionResponse> GetPredictionAsync(PredictionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Get predictions for multiple appointments (batch).</summary>
    Task<IReadOnlyList<PredictionResponse>> GetPredictionsBatchAsync(
        IReadOnlyList<PredictionRequest> requests,
        CancellationToken cancellationToken = default);

    /// <summary>Check endpoint health.</summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}
