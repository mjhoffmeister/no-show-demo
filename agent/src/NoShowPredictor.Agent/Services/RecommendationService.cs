using NoShowPredictor.Agent.Models;
using NoShowPredictor.Agent.Tools;

namespace NoShowPredictor.Agent.Services;

/// <summary>
/// Service for generating scheduling recommendations based on predictions.
/// Implements recommendation rules per tasks T067-T068.
/// 
/// Healthcare operational rules:
/// 1. Specialty-specific overbooking caps (surgery=0%, primary care=15%, etc.)
/// 2. New-patient / long-visit priority escalation
/// 3. Virtual visit de-escalation (telehealth doesn't waste a room)
/// 4. Lead-time-based intervention routing (call vs. text vs. proactive outreach)
/// </summary>
public class RecommendationService
{
    // =========================================================================
    // Specialty overbooking caps — max % of schedule that can be overbooked.
    // Surgery/procedural can't double-book; primary care tolerates more.
    // =========================================================================
    private static readonly Dictionary<string, double> SpecialtyOverbookCaps = new(StringComparer.OrdinalIgnoreCase)
    {
        // Primary care — high volume, short visits, easy to backfill
        ["Family Medicine"] = 0.15,
        ["Internal Medicine"] = 0.15,
        ["Pediatrics"] = 0.15,

        // Behavioral health — flexible scheduling, frequent no-shows
        ["Psychiatry"] = 0.20,

        // Medical specialties — moderate tolerance
        ["Cardiology"] = 0.10,
        ["Dermatology"] = 0.10,
        ["Endocrinology"] = 0.10,
        ["Gastroenterology"] = 0.10,
        ["Neurology"] = 0.10,
        ["Pulmonology"] = 0.10,
        ["Rheumatology"] = 0.10,

        // Surgical / procedural — cannot safely overbook
        ["Orthopedics"] = 0.0,
        ["Urology"] = 0.05,
        ["OB/GYN"] = 0.05,
        ["Ophthalmology"] = 0.05,
    };

    /// <summary>Default cap for specialties not explicitly listed.</summary>
    private const double DefaultOverbookCap = 0.10;
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
    /// Determine the appropriate action based on risk level, appointment context, and operational rules.
    /// 
    /// Rule priority (applied in order):
    /// 1. Virtual visit de-escalation — telehealth appointments get lighter-touch interventions
    /// 2. New-patient / long-visit escalation — high-value slots bump priority up one tier
    /// 3. Lead-time-based routing — determines call vs. text vs. proactive outreach
    /// 4. Base risk rules — High → call, Medium → reminder, Low → no action
    /// </summary>
    private (ActionType action, Priority priority, string rationale) DetermineAction(AppointmentPrediction prediction)
    {
        var isMorning = prediction.AppointmentDateTime.Hour < 12;
        var topFactors = prediction.RiskFactors.Take(3).ToList();
        var isVirtual = prediction.VirtualFlag is "Virtual-Video" or "Virtual-Telephone";
        var isNewPatient = prediction.NewPatientFlag == "NEW PATIENT";
        var isLongVisit = prediction.AppointmentDuration >= 45;
        var leadTimeDays = prediction.LeadTimeDays;

        // -----------------------------------------------------------------
        // Low risk — no action regardless of other factors
        // -----------------------------------------------------------------
        if (prediction.RiskLevel == "Low")
        {
            return (ActionType.NoAction, Priority.Low, string.Empty);
        }

        // -----------------------------------------------------------------
        // Medium risk — reminder protocol, but escalate for high-value slots
        // -----------------------------------------------------------------
        if (prediction.RiskLevel == "Medium")
        {
            // Rule 3: Virtual visit de-escalation — text link is enough
            if (isVirtual)
            {
                return (
                    ActionType.Reminder,
                    Priority.Low,
                    $"Moderate-risk telehealth visit. Send appointment link reminder 1 hour before. {FormatTopFactors(topFactors)}"
                );
            }

            // Rule 2: New-patient or long visit — escalate medium → high
            if (isNewPatient || isLongVisit)
            {
                var slotType = isNewPatient ? "new-patient" : $"{prediction.AppointmentDuration}-minute";
                return (
                    ActionType.ConfirmationCall,
                    Priority.High,
                    $"Moderate-risk {slotType} slot — difficult to backfill same-day. Phone confirmation recommended. {FormatTopFactors(topFactors)}"
                );
            }

            // Rule 4: Lead-time routing for medium risk
            if (leadTimeDays < 3)
            {
                return (
                    ActionType.Reminder,
                    Priority.Medium,
                    $"Moderate risk, short lead time. Send SMS/text reminder — not enough time for phone outreach. {FormatTopFactors(topFactors)}"
                );
            }

            return (
                ActionType.Reminder,
                Priority.Medium,
                $"Moderate risk. Send text and email reminders 24 hours before appointment. {FormatTopFactors(topFactors)}"
            );
        }

        // -----------------------------------------------------------------
        // High risk — full intervention, modulated by context
        // -----------------------------------------------------------------
        var hasHistoricalRisk = topFactors.Any(f =>
            f.Factor.Contains("Historical", StringComparison.OrdinalIgnoreCase) ||
            f.Factor.Contains("No Show", StringComparison.OrdinalIgnoreCase));

        // Rule 3: Virtual visit de-escalation — downgrade to reminder + link
        if (isVirtual)
        {
            return (
                ActionType.Reminder,
                Priority.Medium,
                $"High-risk telehealth visit — no physical room impact. Send video/phone link reminder with 24-hour and 1-hour nudges. {FormatTopFactors(topFactors)}"
            );
        }

        // Overbooking consideration for repeat no-show patients
        if (hasHistoricalRisk)
        {
            return (
                ActionType.Overbook,
                Priority.Urgent,
                $"Patient has high historical no-show risk. Consider overbooking this time slot. {FormatTopFactors(topFactors)}"
            );
        }

        // Rule 2: New-patient or long visit escalation — always urgent
        if (isNewPatient || isLongVisit)
        {
            var slotType = isNewPatient ? "new-patient" : $"{prediction.AppointmentDuration}-minute";
            return (
                ActionType.ConfirmationCall,
                Priority.Urgent,
                $"High-risk {slotType} appointment — significant revenue loss if missed. Immediate phone confirmation required. {FormatTopFactors(topFactors)}"
            );
        }

        // Rule 4: Lead-time-based intervention routing
        if (leadTimeDays > 14)
        {
            return (
                ActionType.ConfirmationCall,
                Priority.High,
                $"High-risk appointment booked {leadTimeDays} days ago. Proactive outreach call recommended 7 days before visit. {FormatTopFactors(topFactors)}"
            );
        }

        if (leadTimeDays < 3)
        {
            return (
                ActionType.Reminder,
                Priority.High,
                $"High-risk appointment with very short lead time. Send immediate SMS confirmation — no time for phone tag. {FormatTopFactors(topFactors)}"
            );
        }

        // Default high-risk: morning gets urgent, afternoon gets high
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
            .Select(g =>
            {
                var specialty = g.First().ProviderSpecialty;
                var cap = SpecialtyOverbookCaps.GetValueOrDefault(specialty, DefaultOverbookCap);
                return new ProviderOverbookAnalysis
                {
                    ProviderName = g.Key,
                    Date = date,
                    TotalAppointments = g.Count(),
                    HighRiskCount = g.Count(p => p.RiskLevel == "High"),
                    MediumRiskCount = g.Count(p => p.RiskLevel == "Medium"),
                    ExpectedNoShows = g.Count(p => p.PredictedNoShow),
                    RecommendedOverbookSlots = CalculateOverbookSlots(g.ToList()),
                    Specialty = specialty,
                    OverbookCapPercentage = cap * 100
                };
            })
            .Where(a => a.HighRiskCount > 0)
            .OrderByDescending(a => a.ExpectedNoShows)
            .ToList();

        return byProvider;
    }

    private static int CalculateOverbookSlots(List<AppointmentPrediction> providerAppointments)
    {
        var highRiskCount = providerAppointments.Count(p => p.RiskLevel == "High");
        var expectedNoShows = providerAppointments.Count(p => p.PredictedNoShow);
        var totalAppointments = providerAppointments.Count;

        // Determine specialty cap from the provider's specialty
        var specialty = providerAppointments.FirstOrDefault()?.ProviderSpecialty ?? string.Empty;
        var cap = SpecialtyOverbookCaps.GetValueOrDefault(specialty, DefaultOverbookCap);

        // Zero cap means overbooking is not allowed for this specialty
        if (cap <= 0)
        {
            return 0;
        }

        // Max overbook slots = floor(total appointments × specialty cap)
        var maxSlots = (int)Math.Floor(totalAppointments * cap);

        // Recommend the lesser of expected no-shows and the specialty cap
        return Math.Min(expectedNoShows, maxSlots);
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
    public string Specialty { get; init; } = string.Empty;
    public double OverbookCapPercentage { get; init; }

    public string Summary => RecommendedOverbookSlots > 0
        ? $"{ProviderName} ({Specialty}): {TotalAppointments} appointments, {ExpectedNoShows} expected no-shows, recommend {RecommendedOverbookSlots} overbook slots (cap: {OverbookCapPercentage:F0}%)"
        : $"{ProviderName} ({Specialty}): {TotalAppointments} appointments, {ExpectedNoShows} expected no-shows — overbooking not recommended (specialty cap: {OverbookCapPercentage:F0}%)";
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
