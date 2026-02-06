using System.ComponentModel;
using NoShowPredictor.Agent.Models;
using NoShowPredictor.Agent.Services;

namespace NoShowPredictor.Agent.Tools;

/// <summary>
/// Agent tool for getting no-show predictions from the ML endpoint.
/// Includes graceful degradation when ML endpoint is unavailable.
/// </summary>
public class PredictionTool
{
    private readonly IMLEndpointClient _mlClient;

    public PredictionTool(IMLEndpointClient mlClient)
    {
        _mlClient = mlClient;
    }

    /// <summary>
    /// Get no-show probability predictions for a list of appointments.
    /// Falls back to historical-based estimates if ML endpoint is unavailable.
    /// </summary>
    /// <param name="appointments">Appointments to predict</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Predictions with risk levels and contributing factors</returns>
    [Description("Get no-show probability predictions for appointments. Call GetAppointmentsByDateRange first to get appointments, then pass them here for predictions.")]
    public async Task<PredictionResult> GetPredictions(
        [Description("List of appointments to get predictions for")] IEnumerable<Appointment> appointments,
        CancellationToken cancellationToken = default)
    {
        var appointmentList = appointments.ToList();
        if (appointmentList.Count == 0)
        {
            return new PredictionResult
            {
                Predictions = [],
                Source = "No appointments provided",
                IsMLBased = false
            };
        }

        try
        {
            var predictions = await _mlClient.GetPredictionsAsync(appointmentList, cancellationToken);

            // Combine appointments with predictions
            var results = appointmentList
                .Zip(predictions, (appt, pred) => new AppointmentPrediction
                {
                    AppointmentId = appt.AppointmentId,
                    PatientName = GetPatientDisplayName(appt.Patient),
                    ProviderName = appt.Provider?.DisplayName ?? "Unknown Provider",
                    AppointmentDateTime = appt.AppointmentDateTime,
                    AppointmentType = appt.AppointmentTypeName,
                    PredictedNoShow = pred.PredictedNoShow,
                    RiskLevel = pred.RiskLevel,
                    RiskFactors = pred.RiskFactors.Select(f => new RiskFactorDisplay
                    {
                        Factor = FormatFactorName(f.FactorName),
                        Value = f.FactorValue,
                        Impact = f.Direction,
                        Importance = f.Contribution
                    }).ToList(),
                    // Operational context for recommendation rules
                    ProviderSpecialty = appt.Provider?.ProviderSpecialty ?? string.Empty,
                    VirtualFlag = appt.VirtualFlag,
                    NewPatientFlag = appt.NewPatientFlag,
                    AppointmentDuration = appt.AppointmentDuration,
                    LeadTimeDays = appt.LeadTimeDays
                })
                .OrderByDescending(p => p.PredictedNoShow)
                .ToList();

            return new PredictionResult
            {
                Predictions = results,
                Source = "ML Model",
                IsMLBased = true
            };
        }
        catch (HttpRequestException ex)
        {
            // ML endpoint unavailable - use fallback
            return CreateFallbackPredictions(appointmentList, $"ML endpoint unavailable: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            // Timeout - use fallback
            return CreateFallbackPredictions(appointmentList, "ML endpoint timeout");
        }
        catch (Exception ex)
        {
            // Other errors - use fallback
            return CreateFallbackPredictions(appointmentList, $"Prediction error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get high-risk appointments (probability > 0.6) for a date range.
    /// </summary>
    [Description("Get only high-risk appointments (no-show probability > 60%) for a date range. This is a convenience method that filters predictions.")]
    public async Task<IReadOnlyList<AppointmentPrediction>> GetHighRiskAppointments(
        [Description("Predictions from GetPredictions")] IEnumerable<AppointmentPrediction> predictions)
    {
        return await Task.FromResult(predictions.Where(p => p.RiskLevel == "High").ToList());
    }

    /// <summary>
    /// Creates fallback predictions based on historical patterns when ML is unavailable.
    /// </summary>
    private static PredictionResult CreateFallbackPredictions(List<Appointment> appointments, string reason)
    {
        var results = appointments.Select(appt =>
        {
            // Calculate risk based on historical factors
            var probability = CalculateFallbackProbability(appt);
            var riskFactors = GetFallbackRiskFactors(appt);

            return new AppointmentPrediction
            {
                AppointmentId = appt.AppointmentId,
                PatientName = GetPatientDisplayName(appt.Patient),
                ProviderName = appt.Provider?.DisplayName ?? "Unknown Provider",
                AppointmentDateTime = appt.AppointmentDateTime,
                AppointmentType = appt.AppointmentTypeName,
                PredictedNoShow = probability > 0.5m,
                RiskLevel = probability switch
                {
                    > 0.6m => "High",
                    > 0.3m => "Medium",
                    _ => "Low"
                },
                RiskFactors = riskFactors,
                // Operational context for recommendation rules
                ProviderSpecialty = appt.Provider?.ProviderSpecialty ?? string.Empty,
                VirtualFlag = appt.VirtualFlag,
                NewPatientFlag = appt.NewPatientFlag,
                AppointmentDuration = appt.AppointmentDuration,
                LeadTimeDays = appt.LeadTimeDays
            };
        })
        .OrderByDescending(p => p.PredictedNoShow)
        .ToList();

        return new PredictionResult
        {
            Predictions = results,
            Source = $"Historical Patterns (Fallback: {reason})",
            IsMLBased = false,
            Warning = "ML prediction unavailable. Using historical pattern estimates which may be less accurate."
        };
    }

    /// <summary>
    /// Calculate probability based on known risk factors when ML is unavailable.
    /// Based on research.md historical patterns.
    /// </summary>
    private static decimal CalculateFallbackProbability(Appointment appt)
    {
        // Base rate from research: ~18% average no-show rate
        decimal probability = 0.18m;

        // Lead time: +1% per day over 7 days
        var leadTimeDays = appt.LeadTimeDays;
        if (leadTimeDays > 7)
        {
            probability += Math.Min(0.15m, (leadTimeDays - 7) * 0.01m);
        }

        // Day of week: Monday +5%, Friday +5%
        var dayOfWeek = appt.AppointmentDate.DayOfWeek;
        if (dayOfWeek == DayOfWeek.Monday || dayOfWeek == DayOfWeek.Friday)
        {
            probability += 0.05m;
        }

        // Time of day: afternoon (2pm-5pm) +5%
        var hour = appt.HourOfDay;
        if (hour >= 14 && hour < 17)
        {
            probability += 0.05m;
        }

        // New patient: +10%
        if (appt.NewPatientFlag == "NEW PATIENT")
        {
            probability += 0.10m;
        }

        // Patient portal engaged: -5%
        if (appt.Patient?.PortalEngaged == true)
        {
            probability -= 0.05m;
        }

        // Insurance type
        if (appt.Insurance?.Sipg1 is "Medicaid" or "Self Pay")
        {
            probability += 0.08m;
        }

        // Clamp between 0.05 and 0.95
        return Math.Clamp(probability, 0.05m, 0.95m);
    }

    /// <summary>
    /// Get risk factors based on appointment attributes for fallback mode.
    /// </summary>
    private static List<RiskFactorDisplay> GetFallbackRiskFactors(Appointment appt)
    {
        var factors = new List<RiskFactorDisplay>();

        if (appt.LeadTimeDays > 14)
        {
            factors.Add(new RiskFactorDisplay
            {
                Factor = "Lead Time",
                Value = $"{appt.LeadTimeDays} days",
                Impact = "increases risk",
                Importance = 0.15m
            });
        }

        var dayOfWeek = appt.AppointmentDate.DayOfWeek;
        if (dayOfWeek is DayOfWeek.Monday or DayOfWeek.Friday)
        {
            factors.Add(new RiskFactorDisplay
            {
                Factor = "Day of Week",
                Value = dayOfWeek.ToString(),
                Impact = "increases risk",
                Importance = 0.10m
            });
        }

        if (appt.HourOfDay >= 14 && appt.HourOfDay < 17)
        {
            factors.Add(new RiskFactorDisplay
            {
                Factor = "Appointment Time",
                Value = "Afternoon",
                Impact = "increases risk",
                Importance = 0.08m
            });
        }

        if (appt.NewPatientFlag == "NEW PATIENT")
        {
            factors.Add(new RiskFactorDisplay
            {
                Factor = "Patient Type",
                Value = "New Patient",
                Impact = "increases risk",
                Importance = 0.12m
            });
        }

        if (appt.Patient?.PortalEngaged == true)
        {
            factors.Add(new RiskFactorDisplay
            {
                Factor = "Portal Engagement",
                Value = "Active",
                Impact = "reduces risk",
                Importance = 0.05m
            });
        }

        if (appt.Insurance?.Sipg1 is "Medicaid" or "Self Pay")
        {
            factors.Add(new RiskFactorDisplay
            {
                Factor = "Insurance Type",
                Value = appt.Insurance.Sipg1,
                Impact = "increases risk",
                Importance = 0.10m
            });
        }

        return factors;
    }

    private static string GetPatientDisplayName(Patient? patient)
    {
        if (patient == null) return "Unknown Patient";

        // Extract name from email if available (synthetic data pattern)
        if (!string.IsNullOrEmpty(patient.PatientEmail))
        {
            var emailParts = patient.PatientEmail.Split('@');
            if (emailParts.Length > 0)
            {
                var namePart = emailParts[0].Replace(".", " ").Replace("_", " ");
                // Remove any trailing numbers from synthetic email usernames (e.g., "roberta62" -> "roberta")
                namePart = System.Text.RegularExpressions.Regex.Replace(namePart, @"\d+", "").Trim();
                if (!string.IsNullOrWhiteSpace(namePart))
                {
                    return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(namePart);
                }
            }
        }

        return $"Patient ID: {patient.PatientId} (no email address)";
    }

    private static string FormatFactorName(string factorName)
    {
        // Convert snake_case to Title Case
        var words = factorName.Split('_');
        return string.Join(" ", words.Select(w =>
            System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(w.ToLowerInvariant())));
    }
}

/// <summary>
/// Result of prediction request including ML availability status.
/// </summary>
public record PredictionResult
{
    public IReadOnlyList<AppointmentPrediction> Predictions { get; init; } = [];
    public string Source { get; init; } = string.Empty;
    public bool IsMLBased { get; init; }
    public string? Warning { get; init; }
}

/// <summary>
/// Combined appointment and prediction information for display.
/// </summary>
public record AppointmentPrediction
{
    public int AppointmentId { get; init; }
    public string PatientName { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public DateTime AppointmentDateTime { get; init; }
    public string AppointmentType { get; init; } = string.Empty;
    /// <summary>
    /// Internal: true if ML model predicted no-show. Used for sorting.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    internal bool PredictedNoShow { get; init; }
    public string RiskLevel { get; init; } = string.Empty;
    public List<RiskFactorDisplay> RiskFactors { get; init; } = [];
    public string FormattedTime => AppointmentDateTime.ToString("h:mm tt");
    public string FormattedDate => AppointmentDateTime.ToString("MMM d");

    // Operational context fields for recommendation rules
    /// <summary>Provider's specialty (e.g., "Family Medicine", "Orthopedics")</summary>
    public string ProviderSpecialty { get; init; } = string.Empty;
    /// <summary>"Virtual-Video", "Virtual-Telephone", or "Non-Virtual"</summary>
    public string VirtualFlag { get; init; } = "Non-Virtual";
    /// <summary>"NEW PATIENT" or "EST PATIENT"</summary>
    public string NewPatientFlag { get; init; } = "EST PATIENT";
    /// <summary>Duration in minutes (15, 30, 45, 60)</summary>
    public int AppointmentDuration { get; init; }
    /// <summary>Days between scheduling and appointment</summary>
    public int LeadTimeDays { get; init; }
}

/// <summary>
/// Risk factor for display purposes.
/// </summary>
public record RiskFactorDisplay
{
    public string Factor { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Impact { get; init; } = string.Empty;
    public decimal Importance { get; init; }
}
