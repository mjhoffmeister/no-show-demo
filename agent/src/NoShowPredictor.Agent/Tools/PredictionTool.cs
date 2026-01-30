using System.ComponentModel;
using NoShowPredictor.Agent.Data;
using NoShowPredictor.Agent.Models;
using NoShowPredictor.Agent.Services;

namespace NoShowPredictor.Agent.Tools;

/// <summary>
/// Tool for getting ML predictions for appointment no-show probability.
/// </summary>
public sealed class PredictionTool
{
    private readonly IAppointmentRepository _repository;
    private readonly IMLEndpointClient _mlClient;
    private readonly ILogger<PredictionTool> _logger;

    public PredictionTool(
        IAppointmentRepository repository,
        IMLEndpointClient mlClient,
        ILogger<PredictionTool> logger)
    {
        _repository = repository;
        _mlClient = mlClient;
        _logger = logger;
    }

    /// <summary>
    /// Get no-show predictions for appointments.
    /// </summary>
    [Description("Get no-show probability predictions for a list of appointment IDs. Returns risk level (Low/Medium/High) and contributing factors.")]
    public async Task<PredictionResult> GetPredictions(
        [Description("List of appointment IDs to get predictions for")] int[] appointmentIds,
        CancellationToken cancellationToken = default)
    {
        if (appointmentIds is null || appointmentIds.Length == 0)
        {
            return new PredictionResult
            {
                Success = false,
                ErrorMessage = "No appointment IDs provided.",
                Predictions = []
            };
        }

        var predictions = new List<PredictionSummary>();
        var failedIds = new List<int>();

        // Check ML endpoint health first
        bool mlHealthy;
        try
        {
            mlHealthy = await _mlClient.IsHealthyAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ML endpoint health check failed, will use fallback predictions");
            mlHealthy = false;
        }

        foreach (var appointmentId in appointmentIds)
        {
            try
            {
                // Get appointment details
                var appointment = await _repository.GetAppointmentByIdAsync(appointmentId, cancellationToken);
                if (appointment is null)
                {
                    failedIds.Add(appointmentId);
                    continue;
                }

                Prediction prediction;

                if (mlHealthy)
                {
                    // Try to get real ML prediction
                    prediction = await GetMLPredictionAsync(appointment, cancellationToken);
                }
                else
                {
                    // Fall back to heuristic-based prediction
                    prediction = GenerateFallbackPrediction(appointment);
                }

                // Save prediction to database
                try
                {
                    await _repository.SavePredictionAsync(prediction, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to save prediction for appointment {AppointmentId}", appointmentId);
                }

                predictions.Add(new PredictionSummary
                {
                    AppointmentId = appointmentId,
                    PatientId = appointment.PatientId,
                    NoShowProbability = prediction.NoShowProbability,
                    RiskLevel = prediction.RiskLevel,
                    RiskFactors = prediction.RiskFactors.Select(rf => new RiskFactorSummary
                    {
                        Factor = rf.Name,
                        Impact = rf.Contribution > 0 ? "Increases risk" : "Decreases risk",
                        Description = rf.Description
                    }).ToList(),
                    AppointmentDate = appointment.AppointmentDate.ToString("dddd, MMMM d, yyyy"),
                    AppointmentTime = appointment.AppointmentStartTime,
                    ProviderName = $"Dr. {appointment.Provider?.ProviderLastName ?? "Unknown"}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting prediction for appointment {AppointmentId}", appointmentId);
                failedIds.Add(appointmentId);
            }
        }

        var result = new PredictionResult
        {
            Success = predictions.Count > 0,
            Predictions = predictions
                .OrderByDescending(p => p.NoShowProbability)
                .ToList(),
            FailedAppointmentIds = failedIds,
            UsedFallback = !mlHealthy
        };

        if (!mlHealthy)
        {
            result = result with
            {
                Warning = "ML endpoint unavailable. Predictions are based on historical patterns and may be less accurate."
            };
        }

        return result;
    }

    /// <summary>
    /// Get prediction for a single appointment using ML endpoint.
    /// </summary>
    private async Task<Prediction> GetMLPredictionAsync(Appointment appointment, CancellationToken cancellationToken)
    {
        // Build prediction request from appointment data
        var request = new PredictionRequest
        {
            AppointmentId = appointment.AppointmentId,
            PatientAgeBucket = appointment.Patient?.PatientAgeBucket ?? "Unknown",
            PatientGender = appointment.Patient?.PatientGender ?? "Unknown",
            DistanceFromClinicMiles = 10.0, // Default, would be calculated from zip codes
            HasActivePortal = appointment.Patient?.PortalEnterpriseId is not null,
            HistoricalNoShowCount = 0, // Would be calculated from patient history
            HistoricalApptCount = 0, // Would be calculated from patient history
            LeadTimeDays = (appointment.AppointmentDate.ToDateTime(TimeOnly.MinValue) - appointment.AppointmentCreatedDateTime).Days,
            DayOfWeek = (int)appointment.AppointmentDate.DayOfWeek,
            HourOfDay = int.TryParse(appointment.AppointmentStartTime.Split(':')[0], out var hour) ? hour : 9,
            AppointmentTypeName = appointment.AppointmentTypeName,
            ProviderSpecialty = appointment.Provider?.ProviderSpecialty ?? "General",
            DepartmentSpecialty = appointment.Department?.DepartmentSpecialty ?? "General",
            VirtualFlag = appointment.VirtualFlag,
            NewPatientFlag = appointment.NewPatientFlag,
            PayerGrouping = "Commercial" // Would come from insurance data
        };

        var response = await _mlClient.GetPredictionAsync(request, cancellationToken);

        return new Prediction
        {
            PredictionId = Guid.NewGuid(),
            AppointmentId = appointment.AppointmentId,
            NoShowProbability = response.NoShowProbability,
            RiskLevel = response.RiskLevel,
            RiskFactors = response.RiskFactors.Select(rf => new RiskFactor
            {
                Name = rf.Name,
                Contribution = rf.Contribution,
                Description = rf.Description
            }).ToList(),
            ModelVersion = response.ModelVersion,
            PredictedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Generate a fallback prediction using heuristics when ML endpoint is unavailable.
    /// </summary>
    private static Prediction GenerateFallbackPrediction(Appointment appointment)
    {
        var riskFactors = new List<RiskFactor>();
        double baseProb = 0.22; // Historical average

        // New patient adjustment
        if (appointment.NewPatientFlag == "NEW PATIENT")
        {
            baseProb += 0.05;
            riskFactors.Add(new RiskFactor
            {
                Name = "New Patient",
                Contribution = 0.05,
                Description = "New patients have higher no-show rates"
            });
        }

        // Day of week adjustment (Monday higher)
        if (appointment.AppointmentDate.DayOfWeek == DayOfWeek.Monday)
        {
            baseProb += 0.03;
            riskFactors.Add(new RiskFactor
            {
                Name = "Monday Appointment",
                Contribution = 0.03,
                Description = "Monday appointments have slightly higher no-show rates"
            });
        }

        // Virtual appointment adjustment (lower no-show)
        if (appointment.VirtualFlag != "Non-Virtual")
        {
            baseProb -= 0.05;
            riskFactors.Add(new RiskFactor
            {
                Name = "Virtual Visit",
                Contribution = -0.05,
                Description = "Virtual appointments have lower no-show rates"
            });
        }

        // Early morning adjustment
        if (appointment.AppointmentStartTime.StartsWith("07") || appointment.AppointmentStartTime.StartsWith("08"))
        {
            baseProb += 0.02;
            riskFactors.Add(new RiskFactor
            {
                Name = "Early Morning",
                Contribution = 0.02,
                Description = "Early morning appointments have slightly higher no-show rates"
            });
        }

        // Lead time adjustment
        var leadDays = (appointment.AppointmentDate.ToDateTime(TimeOnly.MinValue) - appointment.AppointmentCreatedDateTime).Days;
        if (leadDays > 30)
        {
            baseProb += 0.05;
            riskFactors.Add(new RiskFactor
            {
                Name = "Long Lead Time",
                Contribution = 0.05,
                Description = $"Scheduled {leadDays} days ahead - longer lead times increase no-show risk"
            });
        }

        // Portal engagement (if available)
        if (appointment.Patient?.PortalEnterpriseId is null)
        {
            baseProb += 0.03;
            riskFactors.Add(new RiskFactor
            {
                Name = "No Portal Access",
                Contribution = 0.03,
                Description = "Patients without portal engagement have higher no-show rates"
            });
        }

        // Clamp probability
        var probability = Math.Clamp(baseProb, 0.0, 1.0);

        // Determine risk level
        var riskLevel = probability switch
        {
            >= 0.6 => "High",
            >= 0.3 => "Medium",
            _ => "Low"
        };

        return new Prediction
        {
            PredictionId = Guid.NewGuid(),
            AppointmentId = appointment.AppointmentId,
            NoShowProbability = probability,
            RiskLevel = riskLevel,
            RiskFactors = riskFactors,
            ModelVersion = "fallback-heuristic-v1",
            PredictedAt = DateTime.UtcNow
        };
    }
}

/// <summary>
/// Result of a prediction query.
/// </summary>
public sealed record PredictionResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<PredictionSummary> Predictions { get; init; } = [];
    public IReadOnlyList<int> FailedAppointmentIds { get; init; } = [];
    public bool UsedFallback { get; init; }
    public string? Warning { get; init; }
}

/// <summary>
/// Summary of a prediction for display.
/// </summary>
public sealed record PredictionSummary
{
    public int AppointmentId { get; init; }
    public int PatientId { get; init; }
    public double NoShowProbability { get; init; }
    public string RiskLevel { get; init; } = string.Empty;
    public IReadOnlyList<RiskFactorSummary> RiskFactors { get; init; } = [];
    public string AppointmentDate { get; init; } = string.Empty;
    public string AppointmentTime { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
}

/// <summary>
/// Summary of a risk factor for display.
/// </summary>
public sealed record RiskFactorSummary
{
    public string Factor { get; init; } = string.Empty;
    public string Impact { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}
