using System.ComponentModel;
using System.Text.Json;
using NoShowPredictor.Agent.Data;
using NoShowPredictor.Agent.Models;
using NoShowPredictor.Agent.Services;

namespace NoShowPredictor.Agent.Tools;

/// <summary>
/// Focused tool for no-show risk prediction. Combines appointment retrieval 
/// with ML predictions in a single call for efficiency.
/// </summary>
public class NoShowRiskTool
{
    private readonly IAppointmentRepository _repository;
    private readonly IMLEndpointClient _mlClient;

    public NoShowRiskTool(IAppointmentRepository repository, IMLEndpointClient mlClient)
    {
        _repository = repository;
        _mlClient = mlClient;
    }

    /// <summary>
    /// Get no-show risk predictions for scheduled appointments on a given date.
    /// This is the main tool for predicting which patients may not show up.
    /// </summary>
    [Description("Get no-show risk predictions for a date. Returns appointments with their predicted no-show probability and risk factors. Use this to identify which patients need outreach.")]
    public async Task<NoShowRiskResult> GetNoShowRisk(
        [Description("Date expression: 'tomorrow', 'today', 'this week', 'next 3 days', 'Monday', or 'YYYY-MM-DD'")] string dateExpression,
        CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Console.WriteLine($"[Tool] GetNoShowRisk called with: {dateExpression}");

        var parseResult = ParseDateExpression(dateExpression);
        Console.WriteLine($"[Tool] Parsed dates: {parseResult.StartDate} to {parseResult.EndDate}");

        // Get scheduled appointments only
        var appointments = await _repository.GetAppointmentsByDateRangeAsync(
            parseResult.StartDate, parseResult.EndDate, riskLevelFilter: null, cancellationToken);

        var appointmentList = appointments.ToList();
        Console.WriteLine($"[Tool] Found {appointmentList.Count} scheduled appointments in {sw.ElapsedMilliseconds}ms");

        if (appointmentList.Count == 0)
        {
            return new NoShowRiskResult
            {
                DateRange = $"{parseResult.StartDate:yyyy-MM-dd} to {parseResult.EndDate:yyyy-MM-dd}",
                InterpretedAs = parseResult.InterpretedAs,
                TotalAppointments = 0,
                HighRiskCount = 0,
                LowRiskCount = 0,
                HighRiskAppointments = [],
                Source = "No scheduled appointments found",
                IsMLBased = false
            };
        }

        // Get predictions (with fallback if ML unavailable)
        List<AppointmentWithRisk> predictions;
        string source;
        bool isMLBased;
        string? warning = null;

        try
        {
            var mlPredictions = await _mlClient.GetPredictionsAsync(appointmentList, cancellationToken);
            predictions = appointmentList
                .Zip(mlPredictions, (appt, pred) => CreateAppointmentWithRisk(appt, pred))
                .ToList();
            source = "Azure ML Model";
            isMLBased = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Tool] ML endpoint unavailable: {ex.Message}. Using fallback predictions.");
            predictions = appointmentList.Select(CreateFallbackPrediction).ToList();
            source = "Historical Pattern Estimates";
            isMLBased = false;
            warning = "ML prediction endpoint unavailable. Using historical pattern estimates.";
        }

        // Sort by risk and get high-risk appointments for the response
        var sorted = predictions.OrderByDescending(p => p.PredictedNoShow).ToList();
        var highRisk = sorted.Where(p => p.RiskLevel == "High").ToList();
        var lowRisk = sorted.Where(p => p.RiskLevel == "Low").ToList();

        Console.WriteLine($"[Tool] GetNoShowRisk completed in {sw.ElapsedMilliseconds}ms: {highRisk.Count} high risk, {lowRisk.Count} low risk");

        // Determine if this is a multi-day range (use summary mode for 2+ days)
        var daySpan = parseResult.EndDate.DayNumber - parseResult.StartDate.DayNumber;
        var isSummaryMode = daySpan >= 1; // More than 1 day = summary mode

        // Build daily summaries for multi-day ranges
        List<DailySummary>? dailySummaries = null;
        if (isSummaryMode)
        {
            dailySummaries = predictions
                .GroupBy(p => p.AppointmentDate)
                .OrderBy(g => g.Key)
                .Select(g => new DailySummary
                {
                    Date = g.Key.ToString("yyyy-MM-dd"),
                    DayOfWeek = g.Key.DayOfWeek.ToString(),
                    TotalAppointments = g.Count(),
                    HighRiskCount = g.Count(p => p.RiskLevel == "High"),
                    LowRiskCount = g.Count(p => p.RiskLevel == "Low"),
                    ExpectedNoShows = g.Count(p => p.RiskLevel == "High")
                })
                .ToList();
        }

        return new NoShowRiskResult
        {
            DateRange = $"{parseResult.StartDate:yyyy-MM-dd} to {parseResult.EndDate:yyyy-MM-dd}",
            InterpretedAs = parseResult.InterpretedAs,
            TotalAppointments = appointmentList.Count,
            HighRiskCount = highRisk.Count,
            LowRiskCount = lowRisk.Count,
            ExpectedNoShows = highRisk.Count,
            IsSummaryMode = isSummaryMode,
            DailySummaries = dailySummaries,
            // Only include detailed appointments for single-day queries
            HighRiskAppointments = isSummaryMode ? [] : highRisk.Take(20).ToList(),
            // Generate structured card JSON for multi-day forecasts
            ForecastCardJson = isSummaryMode 
                ? GenerateForecastCardJson(parseResult, dailySummaries!, highRisk.Count, lowRisk.Count, appointmentList.Count, source, isMLBased, warning)
                : null,
            Source = source,
            IsMLBased = isMLBased,
            Warning = warning
        };
    }

    /// <summary>
    /// Get recommended scheduling actions based on no-show risk predictions.
    /// Returns prioritized actions: calls to make, overbooking suggestions, etc.
    /// </summary>
    [Description("Get recommended scheduling actions for a date based on no-show risk. Returns prioritized calls to make, overbooking suggestions, and reminder priorities.")]
    public async Task<SchedulingActionsResult> GetSchedulingActions(
        [Description("Date expression: 'tomorrow', 'today', 'this week', or 'YYYY-MM-DD'")] string dateExpression,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[Tool] GetSchedulingActions called with: {dateExpression}");

        // Get the risk data first
        var riskResult = await GetNoShowRisk(dateExpression, cancellationToken);

        var actions = new SchedulingActionsResult
        {
            DateRange = riskResult.DateRange,
            TotalAppointments = riskResult.TotalAppointments,
            ExpectedNoShows = riskResult.HighRiskCount,

            // High-risk: Priority confirmation calls needed
            PriorityCallsNeeded = riskResult.HighRiskAppointments
                .Select(a => new PriorityCallItem
                {
                    PatientName = a.PatientName,
                    AppointmentTime = a.AppointmentTime,
                    Provider = a.ProviderName,
                    RiskLevel = "High",
                    Action = "Priority confirmation call - high no-show risk",
                    TopRiskFactor = a.TopRiskFactors.FirstOrDefault() ?? "Multiple factors"
                })
                .ToList(),

            // Low-risk: Standard reminders
            RemindersNeeded = riskResult.LowRiskCount > 0
                ? $"{riskResult.LowRiskCount} low-risk patients - standard reminder protocol"
                : null,

            // Overbooking recommendation
            OverbookingRecommendation = GenerateOverbookingRecommendation(riskResult),

            Source = riskResult.Source,
            Warning = riskResult.Warning
        };

        return actions;
    }

    /// <summary>
    /// Get a specific patient's no-show risk profile based on their history and upcoming appointments.
    /// </summary>
    [Description("Get a patient's no-show risk profile including their history, risk factors, and recommendations. Search by patient ID.")]
    public async Task<PatientRiskProfile> GetPatientRiskProfile(
        [Description("Patient ID to look up")] int patientId,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[Tool] GetPatientRiskProfile called for patient: {patientId}");

        // Get patient's appointment history
        var appointments = await _repository.GetAppointmentsByPatientAsync(patientId, cancellationToken);
        var appointmentList = appointments.ToList();

        if (appointmentList.Count == 0)
        {
            return new PatientRiskProfile
            {
                PatientId = patientId,
                Found = false,
                Message = "No appointment history found for this patient."
            };
        }

        // Calculate historical no-show rate
        var pastAppointments = appointmentList.Where(a => a.IsPast).ToList();
        var noShows = pastAppointments.Count(a => a.AppointmentStatus == "No Show");
        var noShowRate = pastAppointments.Count > 0
            ? (decimal)noShows / pastAppointments.Count
            : 0m;

        // Get upcoming appointments and their risk
        var upcoming = appointmentList
            .Where(a => !a.IsPast && a.AppointmentStatus == "Scheduled")
            .OrderBy(a => a.AppointmentDateTime)
            .ToList();

        List<AppointmentWithRisk> upcomingWithRisk = [];
        if (upcoming.Count > 0)
        {
            try
            {
                var predictions = await _mlClient.GetPredictionsAsync(upcoming, cancellationToken);
                upcomingWithRisk = upcoming
                    .Zip(predictions, (appt, pred) => CreateAppointmentWithRisk(appt, pred))
                    .ToList();
            }
            catch
            {
                upcomingWithRisk = upcoming.Select(CreateFallbackPrediction).ToList();
            }
        }

        // Identify risk factors
        var riskFactors = new List<string>();
        var patient = appointmentList.First().Patient;

        if (noShowRate > 0.3m)
            riskFactors.Add($"High historical no-show rate: {noShowRate:P0}");
        if (patient?.PortalEngaged != true)
            riskFactors.Add("Not engaged with patient portal");
        if (appointmentList.First().Insurance?.Sipg1 is "Medicaid" or "Self Pay")
            riskFactors.Add($"Insurance type: {appointmentList.First().Insurance?.Sipg1}");
        if (appointmentList.Any(a => a.NewPatientFlag == "NEW PATIENT"))
            riskFactors.Add("New patient - limited history");

        return new PatientRiskProfile
        {
            PatientId = patientId,
            Found = true,
            TotalAppointments = pastAppointments.Count,
            NoShowCount = noShows,
            NoShowRate = noShowRate,
            RiskLevel = noShowRate > 0.5m ? "High" : "Low",
            RiskFactors = riskFactors,
            UpcomingAppointments = upcomingWithRisk,
            Recommendation = GeneratePatientRecommendation(noShowRate, riskFactors, upcomingWithRisk)
        };
    }

    #region Helper Methods

    /// <summary>
    /// Generates structured JSON for the ForecastCard visualization.
    /// </summary>
    private static string GenerateForecastCardJson(
        DateParseResult parseResult,
        List<DailySummary> dailySummaries,
        int totalHighRisk,
        int totalLowRisk,
        int totalAppointments,
        string source,
        bool isMLBased,
        string? warning)
    {
        // Find peak day
        var peakDay = dailySummaries.OrderByDescending(d => d.HighRiskCount).FirstOrDefault();

        // Build forecast days with computed percentages
        var forecastDays = dailySummaries.Select(d =>
        {
            var date = DateOnly.Parse(d.Date);
            return new ForecastDay
            {
                Date = d.Date,
                Label = date.ToString("ddd, MMM d"),
                DayOfWeek = d.DayOfWeek,
                Total = d.TotalAppointments,
                High = d.HighRiskCount,
                Low = d.LowRiskCount,
                HighRiskPercentage = d.TotalAppointments > 0 
                    ? Math.Round(100.0 * d.HighRiskCount / d.TotalAppointments, 1) 
                    : 0,
                IsPeak = d.Date == peakDay?.Date
            };
        }).ToList();

        // Build recommendations
        var recommendations = GenerateForecastRecommendations(dailySummaries, totalHighRisk, peakDay);

        // Format date range nicely
        var startDate = DateOnly.Parse(dailySummaries.First().Date);
        var endDate = DateOnly.Parse(dailySummaries.Last().Date);
        var dateRangeDisplay = startDate.Month == endDate.Month
            ? $"{startDate:MMM d} - {endDate:d}"
            : $"{startDate:MMM d} - {endDate:MMM d}";

        var card = new ForecastCard
        {
            DateRange = dateRangeDisplay,
            TotalAppointments = totalAppointments,
            Summary = new ForecastSummary
            {
                HighRisk = totalHighRisk,
                LowRisk = totalLowRisk,
                ExpectedNoShows = totalHighRisk,
                HighRiskPercentage = totalAppointments > 0 
                    ? Math.Round(100.0 * totalHighRisk / totalAppointments, 1) 
                    : 0
            },
            Days = forecastDays,
            PeakDay = peakDay != null ? new PeakDayInfo
            {
                Date = peakDay.Date,
                DayOfWeek = peakDay.DayOfWeek,
                Label = DateOnly.Parse(peakDay.Date).ToString("ddd, MMM d"),
                HighRiskCount = peakDay.HighRiskCount,
                HighRiskPercentage = peakDay.TotalAppointments > 0
                    ? Math.Round(100.0 * peakDay.HighRiskCount / peakDay.TotalAppointments, 1)
                    : 0
            } : null,
            Recommendations = recommendations,
            Source = source,
            IsMLBased = isMLBased,
            Warning = warning
        };

        return JsonSerializer.Serialize(card, new JsonSerializerOptions 
        { 
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    /// <summary>
    /// Generates prioritized recommendations based on forecast data.
    /// </summary>
    private static List<ForecastRecommendation> GenerateForecastRecommendations(
        List<DailySummary> dailySummaries,
        int totalHighRisk,
        DailySummary? peakDay)
    {
        var recommendations = new List<ForecastRecommendation>();

        // Priority calls recommendation
        if (totalHighRisk > 0)
        {
            var emphasis = peakDay != null && peakDay.HighRiskCount > 0
                ? $"especially {peakDay.DayOfWeek} with {peakDay.HighRiskCount} high-risk"
                : null;

            recommendations.Add(new ForecastRecommendation
            {
                Priority = "high",
                Icon = "call",
                Action = "Priority Confirmation Calls",
                Target = $"{totalHighRisk} high-risk patients",
                Detail = emphasis
            });
        }

        // Overbooking recommendation for peak day
        if (peakDay != null && peakDay.HighRiskCount >= 5)
        {
            var slotsToOverbook = peakDay.HighRiskCount switch
            {
                >= 20 => "3-4",
                >= 10 => "2-3",
                _ => "1-2"
            };

            recommendations.Add(new ForecastRecommendation
            {
                Priority = "medium",
                Icon = "overbook",
                Action = "Overbooking Consideration",
                Target = $"{peakDay.DayOfWeek} ({DateOnly.Parse(peakDay.Date):MMM d})",
                Detail = $"~{slotsToOverbook} slots recommended"
            });
        }

        // Enhanced reminders for medium-risk
        recommendations.Add(new ForecastRecommendation
        {
            Priority = "low",
            Icon = "reminder",
            Action = "Enhanced Reminder Campaigns",
            Target = "Medium-risk patients",
            Detail = $"SMS/email reminders across all {dailySummaries.Count} days"
        });

        return recommendations;
    }

    private static AppointmentWithRisk CreateAppointmentWithRisk(Appointment appt, Prediction pred)
    {
        return new AppointmentWithRisk
        {
            AppointmentId = appt.AppointmentId,
            PatientName = GetPatientDisplayName(appt.Patient),
            ProviderName = appt.Provider?.DisplayName ?? "Unknown",
            DepartmentName = appt.Department?.DepartmentName ?? "Unknown",
            AppointmentDate = appt.AppointmentDate,
            AppointmentTime = appt.AppointmentDateTime.ToString("MMM d h:mm tt"),
            AppointmentType = appt.AppointmentTypeName,
            PredictedNoShow = pred.PredictedNoShow,
            RiskLevel = pred.RiskLevel,
            TopRiskFactors = pred.RiskFactors
                .OrderByDescending(f => f.Contribution)
                .Take(3)
                .Select(f => $"{FormatFactorName(f.FactorName)}: {f.FactorValue}")
                .ToList()
        };
    }

    private static AppointmentWithRisk CreateFallbackPrediction(Appointment appt)
    {
        var probability = CalculateFallbackProbability(appt);
        var factors = GetFallbackRiskFactors(appt);

        return new AppointmentWithRisk
        {
            AppointmentId = appt.AppointmentId,
            PatientName = GetPatientDisplayName(appt.Patient),
            ProviderName = appt.Provider?.DisplayName ?? "Unknown",
            DepartmentName = appt.Department?.DepartmentName ?? "Unknown",
            AppointmentDate = appt.AppointmentDate,
            AppointmentTime = appt.AppointmentDateTime.ToString("MMM d h:mm tt"),
            AppointmentType = appt.AppointmentTypeName,
            PredictedNoShow = probability > 0.5m,
            RiskLevel = probability > 0.5m ? "High" : "Low",
            TopRiskFactors = factors
        };
    }

    private static decimal CalculateFallbackProbability(Appointment appt)
    {
        decimal probability = 0.18m; // Base rate

        if (appt.LeadTimeDays > 7)
            probability += Math.Min(0.15m, (appt.LeadTimeDays - 7) * 0.01m);

        if (appt.AppointmentDate.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Friday)
            probability += 0.05m;

        if (appt.HourOfDay >= 14 && appt.HourOfDay < 17)
            probability += 0.05m;

        if (appt.NewPatientFlag == "NEW PATIENT")
            probability += 0.10m;

        if (appt.Patient?.PortalEngaged == true)
            probability -= 0.05m;

        if (appt.Insurance?.Sipg1 is "Medicaid" or "Self Pay")
            probability += 0.08m;

        return Math.Clamp(probability, 0.05m, 0.95m);
    }

    private static List<string> GetFallbackRiskFactors(Appointment appt)
    {
        var factors = new List<string>();

        if (appt.LeadTimeDays > 14)
            factors.Add($"Long lead time: {appt.LeadTimeDays} days");
        if (appt.AppointmentDate.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Friday)
            factors.Add($"Higher risk day: {appt.AppointmentDate.DayOfWeek}");
        if (appt.HourOfDay >= 14 && appt.HourOfDay < 17)
            factors.Add("Afternoon appointment");
        if (appt.NewPatientFlag == "NEW PATIENT")
            factors.Add("New patient");
        if (appt.Patient?.PortalEngaged != true)
            factors.Add("Not portal engaged");
        if (appt.Insurance?.Sipg1 is "Medicaid" or "Self Pay")
            factors.Add($"Insurance: {appt.Insurance.Sipg1}");

        return factors.Take(3).ToList();
    }

    private static string? GenerateOverbookingRecommendation(NoShowRiskResult risk)
    {
        if (risk.TotalAppointments == 0) return null;

        var noShowRate = (double)risk.HighRiskCount / risk.TotalAppointments;

        if (noShowRate > 0.25)
            return $"Consider overbooking 2-3 slots. Expected {risk.HighRiskCount} no-shows ({noShowRate:P0} rate).";
        if (noShowRate > 0.15)
            return $"Consider overbooking 1-2 slots. Expected {risk.HighRiskCount} no-shows ({noShowRate:P0} rate).";

        return null;
    }

    private static string GeneratePatientRecommendation(decimal noShowRate, List<string> riskFactors, List<AppointmentWithRisk> upcoming)
    {
        if (noShowRate > 0.5m)
            return "High-risk patient. Use priority confirmation calls for all appointments. Consider same-day confirmation.";
        if (noShowRate > 0.25m)
            return "Moderate-risk patient. Send multiple reminders (text + email). Confirm 24-48 hours before.";
        if (!riskFactors.Any())
            return "Low-risk patient. Standard reminder protocol is sufficient.";

        return $"Standard protocol with attention to: {string.Join(", ", riskFactors.Take(2))}";
    }

    private static string GetPatientDisplayName(Patient? patient)
    {
        if (patient == null) return "Unknown Patient";
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
        var words = factorName.Split('_');
        return string.Join(" ", words.Select(w =>
            System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(w.ToLowerInvariant())));
    }

    private static DateParseResult ParseDateExpression(string expression)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var expr = expression.Trim().ToLowerInvariant();

        if (DateOnly.TryParse(expression, out var specificDate))
            return new DateParseResult(specificDate, specificDate, $"specific date {specificDate:yyyy-MM-dd}");

        return expr switch
        {
            "today" => new DateParseResult(today, today, "today"),
            "tomorrow" => new DateParseResult(today.AddDays(1), today.AddDays(1), "tomorrow"),
            "this week" => new DateParseResult(today, today.AddDays(7 - (int)today.DayOfWeek), "this week"),
            "next week" => new DateParseResult(today.AddDays(7 - (int)today.DayOfWeek + 1), today.AddDays(14 - (int)today.DayOfWeek), "next week"),
            _ when expr.Contains("next") && int.TryParse(new string(expr.Where(char.IsDigit).ToArray()), out var days) =>
                new DateParseResult(today, today.AddDays(days - 1), $"next {days} days"),
            _ => new DateParseResult(today.AddDays(1), today.AddDays(1), "tomorrow (default)")
        };
    }

    private record DateParseResult(DateOnly StartDate, DateOnly EndDate, string InterpretedAs);

    #endregion
}

#region Result Records

/// <summary>
/// Result of no-show risk prediction for a date range.
/// </summary>
public record NoShowRiskResult
{
    public string DateRange { get; init; } = string.Empty;
    public string InterpretedAs { get; init; } = string.Empty;
    public int TotalAppointments { get; init; }
    public int HighRiskCount { get; init; }
    public int LowRiskCount { get; init; }
    public int ExpectedNoShows { get; init; }
    /// <summary>True for multi-day ranges (weekly forecasts) - returns summaries instead of details</summary>
    public bool IsSummaryMode { get; init; }
    /// <summary>Daily breakdown for multi-day ranges</summary>
    public List<DailySummary>? DailySummaries { get; init; }
    /// <summary>Detailed high-risk appointments (single-day only)</summary>
    public List<AppointmentWithRisk> HighRiskAppointments { get; init; } = [];
    /// <summary>Structured JSON for rich frontend card visualization (multi-day only)</summary>
    public string? ForecastCardJson { get; init; }
    public string Source { get; init; } = string.Empty;
    public bool IsMLBased { get; init; }
    public string? Warning { get; init; }
}

/// <summary>
/// Daily summary for multi-day forecasts.
/// </summary>
public record DailySummary
{
    public string Date { get; init; } = string.Empty;
    public string DayOfWeek { get; init; } = string.Empty;
    public int TotalAppointments { get; init; }
    public int HighRiskCount { get; init; }
    public int LowRiskCount { get; init; }
    public int ExpectedNoShows { get; init; }
}

/// <summary>
/// Appointment with risk prediction details.
/// </summary>
public record AppointmentWithRisk
{
    public int AppointmentId { get; init; }
    public string PatientName { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public string DepartmentName { get; init; } = string.Empty;
    public string AppointmentTime { get; init; } = string.Empty;
    public string AppointmentType { get; init; } = string.Empty;
    [System.Text.Json.Serialization.JsonIgnore]
    internal DateOnly AppointmentDate { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    internal bool PredictedNoShow { get; init; }
    public string RiskLevel { get; init; } = string.Empty;
    public List<string> TopRiskFactors { get; init; } = [];
}

/// <summary>
/// Scheduling actions based on risk predictions.
/// </summary>
public record SchedulingActionsResult
{
    public string DateRange { get; init; } = string.Empty;
    public int TotalAppointments { get; init; }
    public int ExpectedNoShows { get; init; }
    public List<PriorityCallItem> PriorityCallsNeeded { get; init; } = [];
    public string? RemindersNeeded { get; init; }
    public string? OverbookingRecommendation { get; init; }
    public string Source { get; init; } = string.Empty;
    public string? Warning { get; init; }
}

/// <summary>
/// A specific action item for scheduling coordination.
/// </summary>
public record PriorityCallItem
{
    public string PatientName { get; init; } = string.Empty;
    public string AppointmentTime { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string RiskLevel { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string TopRiskFactor { get; init; } = string.Empty;
}

/// <summary>
/// Patient's risk profile based on history.
/// </summary>
public record PatientRiskProfile
{
    public int PatientId { get; init; }
    public bool Found { get; init; }
    public string? Message { get; init; }
    public int TotalAppointments { get; init; }
    public int NoShowCount { get; init; }
    public decimal NoShowRate { get; init; }
    public string RiskLevel { get; init; } = string.Empty;
    public List<string> RiskFactors { get; init; } = [];
    public List<AppointmentWithRisk> UpcomingAppointments { get; init; } = [];
    public string Recommendation { get; init; } = string.Empty;
}

#endregion
