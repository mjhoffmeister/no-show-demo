using NoShowPredictor.Agent.Models;
using NoShowPredictor.Agent.Tools;

namespace NoShowPredictor.Agent.Services;

/// <summary>
/// Service for generating scheduling recommendations based on predictions.
/// Implements recommendation rules per tasks T067-T068.
/// </summary>
public class RecommendationService
{
    /// <summary>
    /// Generate recommendations for appointments based on their predictions.
    /// </summary>
    /// <param name="predictions">Appointment predictions to analyze</param>
    /// <returns>List of recommendations sorted by priority</returns>
    public IReadOnlyList<Recommendation> GenerateRecommendations(IEnumerable<AppointmentPrediction> predictions)
    {
        var recommendations = new List<Recommendation>();

        foreach (var prediction in predictions)
        {
            var recommendation = GenerateRecommendation(prediction);
            if (recommendation != null)
            {
                recommendations.Add(recommendation);
            }
        }

        // Sort by priority (Urgent first) then by risk (predicted no-show first)
        return recommendations
            .OrderBy(r => r.Priority)
            .ThenByDescending(r => r.PredictedNoShow)
            .ToList();
    }

    /// <summary>
    /// Generate a single recommendation for an appointment prediction.
    /// </summary>
    private Recommendation? GenerateRecommendation(AppointmentPrediction prediction)
    {
        // Apply recommendation rules from T068
        var (actionType, priority, rationale) = DetermineAction(prediction);

        if (actionType == ActionType.NoAction)
        {
            return null;
        }

        return new Recommendation
        {
            AppointmentId = prediction.AppointmentId,
            ActionType = actionType,
            Priority = priority,
            Rationale = rationale,
            PatientName = prediction.PatientName,
            ProviderName = prediction.ProviderName,
            AppointmentDateTime = prediction.AppointmentDateTime,
            PredictedNoShow = prediction.PredictedNoShow
        };
    }

    /// <summary>
    /// Determine the appropriate action based on risk level and contributing factors.
    /// Rules from T068:
    /// - High risk + morning → ConfirmationCall (Urgent)
    /// - High risk + afternoon → ConfirmationCall (High)
    /// - High risk + history of no-shows → Overbook consideration
    /// - Medium risk → Reminder (Medium)
    /// - Low risk → NoAction
    /// </summary>
    private (ActionType action, Priority priority, string rationale) DetermineAction(AppointmentPrediction prediction)
    {
        var isMorning = prediction.AppointmentDateTime.Hour < 12;
        var isAfternoon = prediction.AppointmentDateTime.Hour >= 12 && prediction.AppointmentDateTime.Hour < 17;
        var topFactors = prediction.RiskFactors.Take(3).ToList();

        // High risk - ML predicted no-show
        if (prediction.RiskLevel == "High")
        {
            var hasHistoricalRisk = topFactors.Any(f =>
                f.Factor.Contains("Historical", StringComparison.OrdinalIgnoreCase) ||
                f.Factor.Contains("No Show", StringComparison.OrdinalIgnoreCase));

            // Check if this provider has multiple high-risk slots (would need aggregation)
            // For now, use historical risk as a proxy for overbooking consideration
            if (hasHistoricalRisk)
            {
                return (
                    ActionType.Overbook,
                    Priority.Urgent,
                    $"Patient has high historical no-show risk. Consider overbooking this time slot or double-booking with a standby patient. Top factors: {FormatTopFactors(topFactors)}"
                );
            }

            if (isMorning)
            {
                return (
                    ActionType.ConfirmationCall,
                    Priority.Urgent,
                    $"High-risk morning appointment. Priority phone call recommended to confirm attendance. {FormatTopFactors(topFactors)}"
                );
            }

            return (
                ActionType.ConfirmationCall,
                Priority.High,
                $"High-risk appointment. Phone call confirmation recommended. {FormatTopFactors(topFactors)}"
            );
        }

        // Medium risk - use reminder protocol
        if (prediction.RiskLevel == "Medium")
        {
            return (
                ActionType.Reminder,
                Priority.Medium,
                $"Moderate risk. Send text and email reminders 24 hours before appointment. {FormatTopFactors(topFactors)}"
            );
        }

        // Low risk (<30%) - no action needed
        return (ActionType.NoAction, Priority.Low, string.Empty);
    }

    private static string FormatTopFactors(List<RiskFactorDisplay> factors)
    {
        if (factors.Count == 0) return string.Empty;

        return "Key factors: " + string.Join(", ", factors.Select(f =>
            $"{f.Factor} ({f.Impact.ToLowerInvariant()} risk)"));
    }

    /// <summary>
    /// Generate provider-level aggregation for overbooking analysis.
    /// Groups appointments by provider and identifies time slots with multiple high-risk patients.
    /// </summary>
    public IReadOnlyList<ProviderOverbookAnalysis> AnalyzeProviderSchedules(
        IEnumerable<AppointmentPrediction> predictions,
        DateOnly date)
    {
        var byProvider = predictions
            .Where(p => DateOnly.FromDateTime(p.AppointmentDateTime) == date)
            .GroupBy(p => p.ProviderName)
            .Select(g => new ProviderOverbookAnalysis
            {
                ProviderName = g.Key,
                Date = date,
                TotalAppointments = g.Count(),
                HighRiskCount = g.Count(p => p.RiskLevel == "High"),
                MediumRiskCount = g.Count(p => p.RiskLevel == "Medium"),
                ExpectedNoShows = g.Count(p => p.PredictedNoShow),
                RecommendedOverbookSlots = CalculateOverbookSlots(g.ToList())
            })
            .Where(a => a.HighRiskCount > 0)
            .OrderByDescending(a => a.ExpectedNoShows)
            .ToList();

        return byProvider;
    }

    private static int CalculateOverbookSlots(List<AppointmentPrediction> providerAppointments)
    {
        // Simple rule: recommend 1 overbook slot per 2 high-risk appointments
        var highRiskCount = providerAppointments.Count(p => p.RiskLevel == "High");
        var expectedNoShows = providerAppointments.Count(p => p.PredictedNoShow);

        // Expected no-shows to determine overbook recommendation
        return Math.Min(expectedNoShows, highRiskCount / 2 + 1);
    }

    /// <summary>
    /// Generate patient-specific recommendation based on their history and upcoming appointment.
    /// </summary>
    public PatientRecommendation GeneratePatientRecommendation(
        AppointmentPrediction prediction,
        PatientNoShowStats stats)
    {
        var baseRecommendation = GenerateRecommendation(prediction);

        var additionalActions = new List<string>();

        // Add patient-specific recommendations based on history
        if (stats.NoShowRate > 0.5m)
        {
            additionalActions.Add("Consider requiring advance payment or deposit for future appointments");
            additionalActions.Add("Offer transportation assistance if applicable");
        }
        else if (stats.NoShowRate > 0.3m)
        {
            additionalActions.Add("Enroll patient in automated reminder system");
            additionalActions.Add("Offer flexible rescheduling options");
        }

        if (stats.NoShowCount > 3)
        {
            additionalActions.Add("Schedule follow-up call to understand barriers to attendance");
        }

        return new PatientRecommendation
        {
            PatientName = prediction.PatientName,
            AppointmentId = prediction.AppointmentId,
            CurrentRisk = prediction.RiskLevel,
            PredictedNoShow = prediction.PredictedNoShow,
            HistoricalNoShowRate = stats.NoShowRate,
            HistoricalNoShowCount = stats.NoShowCount,
            PrimaryAction = baseRecommendation?.ActionType ?? ActionType.NoAction,
            PrimaryRationale = baseRecommendation?.Rationale ?? "Low risk - standard protocols apply",
            AdditionalActions = additionalActions
        };
    }
}

/// <summary>
/// Provider schedule analysis for overbooking decisions.
/// </summary>
public record ProviderOverbookAnalysis
{
    public string ProviderName { get; init; } = string.Empty;
    public DateOnly Date { get; init; }
    public int TotalAppointments { get; init; }
    public int HighRiskCount { get; init; }
    public int MediumRiskCount { get; init; }
    public int ExpectedNoShows { get; init; }
    public int RecommendedOverbookSlots { get; init; }

    public string Summary => $"{ProviderName}: {TotalAppointments} appointments, {HighRiskCount} high-risk, {ExpectedNoShows} expected no-shows, recommend {RecommendedOverbookSlots} overbook slots";
}

/// <summary>
/// Patient-specific recommendation with history context.
/// </summary>
public record PatientRecommendation
{
    public string PatientName { get; init; } = string.Empty;
    public int AppointmentId { get; init; }
    public string CurrentRisk { get; init; } = string.Empty;
    [System.Text.Json.Serialization.JsonIgnore]
    internal bool PredictedNoShow { get; init; }
    public decimal HistoricalNoShowRate { get; init; }
    public int HistoricalNoShowCount { get; init; }
    public ActionType PrimaryAction { get; init; }
    public string PrimaryRationale { get; init; } = string.Empty;
    public List<string> AdditionalActions { get; init; } = [];
}
