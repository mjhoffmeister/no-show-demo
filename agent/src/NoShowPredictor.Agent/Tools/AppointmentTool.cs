using System.ComponentModel;
using System.Text.RegularExpressions;
using NoShowPredictor.Agent.Data;
using NoShowPredictor.Agent.Models;

namespace NoShowPredictor.Agent.Tools;

/// <summary>
/// Agent tool for retrieving appointment data from the database.
/// Includes data quality warnings and date range validation.
/// </summary>
public partial class AppointmentTool
{
    private readonly IAppointmentRepository _repository;

    public AppointmentTool(IAppointmentRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Get appointments for a date range with optional risk level filtering.
    /// Includes data quality warnings and prediction limit warnings.
    /// </summary>
    /// <param name="dateExpression">Date expression like "tomorrow", "today", "next week", "2026-01-29", "next 3 days"</param>
    /// <param name="riskLevelFilter">Optional filter: "High", "Medium", "Low", or null for all</param>
    /// <returns>List of appointments with patient, provider, and department information plus any warnings</returns>
    [Description("Get scheduled appointments for a date range. Supports natural language dates like 'tomorrow', 'today', 'this week', 'next 3 days', or specific dates. Returns warnings for dates >2 weeks out or missing data.")]
    public async Task<AppointmentQueryResult> GetAppointmentsByDateRange(
        [Description("Date expression: 'tomorrow', 'today', 'this week', 'next week', 'next N days', or 'YYYY-MM-DD'")] string dateExpression,
        [Description("Optional risk level filter: 'High', 'Medium', 'Low', or empty for all")] string? riskLevelFilter = null,
        CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Console.WriteLine($"[Tool] GetAppointmentsByDateRange called with: {dateExpression}, filter: {riskLevelFilter}");

        var parseResult = ParseDateExpression(dateExpression);
        Console.WriteLine($"[Tool] Parsed dates: {parseResult.StartDate} to {parseResult.EndDate} (took {sw.ElapsedMilliseconds}ms)");

        var appointments = await _repository.GetAppointmentsByDateRangeAsync(parseResult.StartDate, parseResult.EndDate, riskLevelFilter, cancellationToken);
        Console.WriteLine($"[Tool] DB query returned {appointments.Count} appointments in {sw.ElapsedMilliseconds}ms");

        var warnings = new List<string>();

        // Add clarification warning if date was ambiguous or unrecognized
        if (parseResult.NeedsClarification)
        {
            warnings.Add(parseResult.ClarificationMessage!);
        }

        // Date range validation: warn for predictions >2 weeks out
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var daysOut = parseResult.EndDate.DayNumber - today.DayNumber;
        if (daysOut > 14)
        {
            warnings.Add($"Date range extends {daysOut} days out. ML predictions become less accurate beyond 2 weeks; consider focusing on appointments within the next 14 days for actionable insights.");
        }

        // Data quality warnings
        var qualityIssues = AnalyzeDataQuality(appointments.ToList());
        if (qualityIssues.Count > 0)
        {
            warnings.AddRange(qualityIssues);
        }

        Console.WriteLine($"[Tool] GetAppointmentsByDateRange total execution: {sw.ElapsedMilliseconds}ms");

        // Build summaries instead of returning full appointment objects (reduces payload size dramatically)
        var appointmentList = appointments.ToList();

        var byProvider = appointmentList
            .Where(a => a.Provider != null)
            .GroupBy(a => new { a.ProviderId, Name = $"{a.Provider!.ProviderFirstName} {a.Provider.ProviderLastName}".Trim() })
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => new ProviderSummary { ProviderId = g.Key.ProviderId, ProviderName = g.Key.Name, AppointmentCount = g.Count() })
            .ToList();

        var byDepartment = appointmentList
            .Where(a => a.Department != null)
            .GroupBy(a => new { a.DepartmentId, a.Department!.DepartmentName })
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => new DepartmentSummary { DepartmentId = g.Key.DepartmentId, DepartmentName = g.Key.DepartmentName ?? "Unknown", AppointmentCount = g.Count() })
            .ToList();

        var byType = appointmentList
            .GroupBy(a => a.AppointmentTypeName)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => new TypeSummary { TypeName = g.Key ?? "Unknown", Count = g.Count() })
            .ToList();

        var byTimeSlot = appointmentList
            .GroupBy(a => a.HourOfDay switch
            {
                < 9 => "Early (before 9am)",
                < 12 => "Morning (9am-12pm)",
                < 15 => "Afternoon (12pm-3pm)",
                < 18 => "Late Afternoon (3pm-6pm)",
                _ => "Evening (after 6pm)"
            })
            .ToDictionary(g => g.Key, g => g.Count());

        var byPatientStatus = appointmentList
            .GroupBy(a => a.NewPatientFlag ?? "Unknown")
            .ToDictionary(g => g.Key, g => g.Count());

        return new AppointmentQueryResult
        {
            TotalCount = appointments.Count,
            DateRange = $"{parseResult.StartDate:yyyy-MM-dd} to {parseResult.EndDate:yyyy-MM-dd}",
            Warnings = warnings,
            InterpretedDateExpression = parseResult.InterpretedAs,
            ByProvider = byProvider,
            ByDepartment = byDepartment,
            ByType = byType,
            ByTimeSlot = byTimeSlot,
            ByPatientStatus = byPatientStatus,
            SampleAppointmentIds = appointmentList.Take(5).Select(a => a.AppointmentId).ToList()
        };
    }

    /// <summary>
    /// Get appointments for a specific provider within a date range.
    /// </summary>
    [Description("Get scheduled appointments for a specific provider by their ID within a date range.")]
    public async Task<IReadOnlyList<Appointment>> GetAppointmentsByProvider(
        [Description("Provider ID")] int providerId,
        [Description("Date expression: 'tomorrow', 'today', 'this week', or 'YYYY-MM-DD'")] string dateExpression,
        CancellationToken cancellationToken = default)
    {
        var parseResult = ParseDateExpression(dateExpression);
        return await _repository.GetAppointmentsByProviderAsync(providerId, parseResult.StartDate, parseResult.EndDate, cancellationToken);
    }

    /// <summary>
    /// Get all appointments for a patient (historical and upcoming).
    /// </summary>
    [Description("Get all appointments for a specific patient, including past and future appointments.")]
    public async Task<IReadOnlyList<Appointment>> GetAppointmentsByPatient(
        [Description("Patient ID")] int patientId,
        CancellationToken cancellationToken = default)
    {
        return await _repository.GetAppointmentsByPatientAsync(patientId, cancellationToken);
    }

    /// <summary>
    /// Search for patients by name.
    /// </summary>
    [Description("Search for patients by name to find their patient ID.")]
    public async Task<IReadOnlyList<Patient>> SearchPatients(
        [Description("Patient name or partial name to search for")] string nameQuery,
        CancellationToken cancellationToken = default)
    {
        return await _repository.SearchPatientsAsync(nameQuery, cancellationToken);
    }

    /// <summary>
    /// Get patient no-show statistics.
    /// </summary>
    [Description("Get a patient's historical no-show statistics including total appointments, no-shows, and no-show rate.")]
    public async Task<PatientNoShowStats> GetPatientNoShowStats(
        [Description("Patient ID")] int patientId,
        CancellationToken cancellationToken = default)
    {
        var (total, noShows, rate) = await _repository.GetPatientNoShowStatsAsync(patientId, cancellationToken);
        return new PatientNoShowStats
        {
            PatientId = patientId,
            TotalAppointments = total,
            NoShowCount = noShows,
            NoShowRate = rate
        };
    }

    /// <summary>
    /// Get weekly forecast - daily aggregation of appointments for the next 7 days.
    /// Includes anomaly detection and high-risk day explanations.
    /// </summary>
    [Description("Get a 7-day forecast showing appointment counts per day with anomaly detection and risk explanations for capacity planning.")]
    public async Task<WeeklyForecastResult> GetWeeklyForecast(
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var forecasts = new List<DailyForecast>();

        for (int i = 0; i < 7; i++)
        {
            var date = today.AddDays(i);
            var appointments = await _repository.GetAppointmentsByDateRangeAsync(date, date, null, cancellationToken);

            var forecast = new DailyForecast
            {
                Date = date,
                DayOfWeek = date.DayOfWeek.ToString(),
                TotalAppointments = appointments.Count,
                RiskFactors = GetDayRiskFactors(date, appointments.ToList())
            };

            forecasts.Add(forecast);
        }

        // Calculate average for anomaly detection
        var avgAppointments = (decimal)forecasts.Average(f => f.TotalAppointments);
        var anomalyThreshold = avgAppointments * 1.3m; // 30% above average

        // Identify anomalies and calculate overall statistics
        foreach (var forecast in forecasts)
        {
            forecast.IsAnomaly = forecast.TotalAppointments > anomalyThreshold;
            if (forecast.IsAnomaly)
            {
                forecast.AnomalyReason = $"Unusually high volume ({forecast.TotalAppointments} appointments vs average of {avgAppointments:F0})";
            }
        }

        return new WeeklyForecastResult
        {
            Forecasts = forecasts,
            TotalAppointments = forecasts.Sum(f => f.TotalAppointments),
            AveragePerDay = avgAppointments,
            HighRiskDays = forecasts.Where(f => f.IsHighRiskDay || f.IsAnomaly).ToList(),
            Summary = GenerateForecastSummary(forecasts)
        };
    }

    private static List<string> GetDayRiskFactors(DateOnly date, List<Appointment> appointments)
    {
        var factors = new List<string>();
        var dayOfWeek = date.DayOfWeek;

        // Monday effect - historically higher no-shows
        if (dayOfWeek == System.DayOfWeek.Monday)
        {
            factors.Add("Monday appointments have historically higher no-show rates (+5%)");
        }

        // Friday effect
        if (dayOfWeek == System.DayOfWeek.Friday)
        {
            factors.Add("Friday appointments may have elevated no-show risk (+5%) due to weekend plans");
        }

        // Check for holiday proximity
        var holidayProximityFactor = GetHolidayProximityFactor(date);
        if (holidayProximityFactor != null)
        {
            factors.Add(holidayProximityFactor);
        }

        // Afternoon-heavy schedule
        var afternoonAppts = appointments.Count(a => a.HourOfDay >= 14 && a.HourOfDay < 17);
        if (appointments.Count > 0 && (double)afternoonAppts / appointments.Count > 0.5)
        {
            factors.Add("High concentration of afternoon appointments (historically higher no-show times)");
        }

        // High lead time appointments
        var longLeadAppts = appointments.Count(a => a.LeadTimeDays > 14);
        if (appointments.Count > 0 && (double)longLeadAppts / appointments.Count > 0.3)
        {
            factors.Add($"{longLeadAppts} appointments scheduled >2 weeks ago (higher no-show risk with longer lead times)");
        }

        // New patient percentage
        var newPatients = appointments.Count(a => a.NewPatientFlag == "NEW PATIENT");
        if (appointments.Count > 0 && (double)newPatients / appointments.Count > 0.25)
        {
            factors.Add($"{newPatients} new patient appointments ({(double)newPatients / appointments.Count:P0} of day - new patients have higher no-show rates)");
        }

        return factors;
    }

    private static string? GetHolidayProximityFactor(DateOnly date)
    {
        var year = date.Year;

        // Common US holidays and high no-show periods
        var holidayPeriods = new Dictionary<(int Month, int StartDay, int EndDay), string>
        {
            { (12, 20, 31), "Holiday season (Dec 20-31): +15% no-show risk" },
            { (1, 1, 5), "Post-New Year period: +15% no-show risk" },
            { (7, 1, 15), "Summer vacation period: +8% no-show risk" },
            { (4, 1, 15), "Tax season: +3% no-show risk due to competing priorities" }
        };

        foreach (var ((month, startDay, endDay), factor) in holidayPeriods)
        {
            if (date.Month == month && date.Day >= startDay && date.Day <= endDay)
            {
                return factor;
            }
        }

        // Check for Monday after major holidays (Memorial Day weekend, Labor Day weekend, etc.)
        // This is a simplified check - in production would use actual holiday calendar
        if (date.DayOfWeek == System.DayOfWeek.Monday)
        {
            // Memorial Day (last Monday of May)
            if (date.Month == 5 && date.Day > 24)
            {
                return "Monday after Memorial Day weekend: +10% no-show risk";
            }
            // Labor Day (first Monday of September)
            if (date.Month == 9 && date.Day <= 7)
            {
                return "Labor Day or post-Labor Day Monday: +10% no-show risk";
            }
        }

        return null;
    }

    private static string GenerateForecastSummary(List<DailyForecast> forecasts)
    {
        var total = forecasts.Sum(f => f.TotalAppointments);
        var avg = (decimal)forecasts.Average(f => f.TotalAppointments);
        var highRiskDays = forecasts.Where(f => f.IsHighRiskDay || f.IsAnomaly).ToList();
        var anomalyDays = forecasts.Where(f => f.IsAnomaly).ToList();

        var summary = $"Weekly total: {total} appointments across 7 days (avg {avg:F0}/day).";

        if (highRiskDays.Count > 0)
        {
            summary += $" {highRiskDays.Count} high-risk days identified.";
        }

        if (anomalyDays.Count > 0)
        {
            var dayNames = string.Join(", ", anomalyDays.Select(d => d.DayOfWeek));
            summary += $" Anomalous volume on: {dayNames}.";
        }

        return summary;
    }

    private static DateParseResult ParseDateExpression(string expression)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var expr = expression.Trim().ToLowerInvariant();

        // Handle specific date format
        if (DateOnly.TryParse(expression, out var specificDate))
        {
            return new DateParseResult(specificDate, specificDate, $"specific date {specificDate:yyyy-MM-dd}");
        }

        // Handle natural language expressions
        return expr switch
        {
            "today" => new DateParseResult(today, today, "today"),
            "tomorrow" => new DateParseResult(today.AddDays(1), today.AddDays(1), "tomorrow"),
            "yesterday" => new DateParseResult(today.AddDays(-1), today.AddDays(-1), "yesterday"),
            "this week" => new DateParseResult(today, today.AddDays(7 - (int)today.DayOfWeek),
                $"this week ({today:MMM d} - {today.AddDays(7 - (int)today.DayOfWeek):MMM d})"),
            "next week" => new DateParseResult(
                today.AddDays(7 - (int)today.DayOfWeek + 1),
                today.AddDays(14 - (int)today.DayOfWeek),
                $"next week ({today.AddDays(7 - (int)today.DayOfWeek + 1):MMM d} - {today.AddDays(14 - (int)today.DayOfWeek):MMM d})"),
            _ when NextNDaysRegex().Match(expr) is { Success: true } match =>
                CreateNextNDaysResult(today, int.Parse(match.Groups[1].Value)),
            _ when TryParseDayOfWeek(expr, today, out var dayResult) => dayResult,
            _ => CreateAmbiguousResult(today, expression) // Return today with clarification warning
        };
    }

    private static DateParseResult CreateNextNDaysResult(DateOnly today, int days)
    {
        var endDate = today.AddDays(days - 1);
        return new DateParseResult(today, endDate, $"next {days} days ({today:MMM d} - {endDate:MMM d})");
    }

    private static bool TryParseDayOfWeek(string expr, DateOnly today, out DateParseResult result)
    {
        // Try to match day names like "monday", "tuesday", "next monday", etc.
        var dayPatterns = new Dictionary<string, DayOfWeek>
        {
            { "monday", DayOfWeek.Monday },
            { "tuesday", DayOfWeek.Tuesday },
            { "wednesday", DayOfWeek.Wednesday },
            { "thursday", DayOfWeek.Thursday },
            { "friday", DayOfWeek.Friday },
            { "saturday", DayOfWeek.Saturday },
            { "sunday", DayOfWeek.Sunday }
        };

        foreach (var (pattern, dayOfWeek) in dayPatterns)
        {
            if (expr.Contains(pattern))
            {
                var targetDate = GetNextOccurrence(today, dayOfWeek, expr.Contains("next"));
                result = new DateParseResult(targetDate, targetDate, $"{targetDate.DayOfWeek} ({targetDate:MMM d})");
                return true;
            }
        }

        result = default!;
        return false;
    }

    private static DateOnly GetNextOccurrence(DateOnly from, DayOfWeek dayOfWeek, bool nextWeek)
    {
        var daysUntil = ((int)dayOfWeek - (int)from.DayOfWeek + 7) % 7;
        if (daysUntil == 0 && !nextWeek) daysUntil = 0; // Today if same day
        else if (daysUntil == 0 || nextWeek) daysUntil += 7; // Next week's occurrence
        return from.AddDays(daysUntil);
    }

    private static DateParseResult CreateAmbiguousResult(DateOnly today, string expression)
    {
        // Return today but with a clarification message
        var clarification = $"The date expression '{expression}' was not clearly understood. " +
            $"Showing results for today ({today:MMM d, yyyy}). " +
            $"For specific dates, try: 'tomorrow', 'this week', 'next week', 'next 3 days', 'Monday', 'next Friday', or a date like '{today.AddDays(1):yyyy-MM-dd}'.";

        return new DateParseResult(today, today, $"today ('{expression}' was ambiguous)")
        {
            NeedsClarification = true,
            ClarificationMessage = clarification
        };
    }

    /// <summary>
    /// Analyze appointment data quality and return warnings for missing demographics.
    /// </summary>
    private static List<string> AnalyzeDataQuality(List<Appointment> appointments)
    {
        var warnings = new List<string>();

        if (appointments.Count == 0) return warnings;

        // Check for missing patient demographics
        var missingInsurance = appointments.Count(a => a.Insurance == null);
        if (missingInsurance > 0)
        {
            var pct = (decimal)missingInsurance / appointments.Count;
            if (pct >= 0.1m) // More than 10%
            {
                warnings.Add($"Data quality: {missingInsurance} appointments ({pct:P0}) missing insurance information - predictions may be less accurate for these.");
            }
        }

        // Check for missing patient portal status
        var missingPortal = appointments.Count(a => a.Patient?.PortalLastLogin == null);
        if (missingPortal > 0)
        {
            var pct = (decimal)missingPortal / appointments.Count;
            if (pct >= 0.2m) // More than 20%
            {
                warnings.Add($"Data quality: {missingPortal} patients ({pct:P0}) have unknown portal engagement - a key predictor for no-shows.");
            }
        }

        // Check for new patients (limited history)
        var newPatients = appointments.Count(a => a.NewPatientFlag == "NEW PATIENT");
        if (newPatients > 0)
        {
            var pct = (decimal)newPatients / appointments.Count;
            if (pct >= 0.15m) // More than 15%
            {
                warnings.Add($"Note: {newPatients} appointments ({pct:P0}) are for new patients with limited history - predictions rely more on demographic factors.");
            }
        }

        // Check for long lead time appointments (potentially stale)
        var longLead = appointments.Count(a => a.LeadTimeDays > 30);
        if (longLead > 0)
        {
            var pct = (decimal)longLead / appointments.Count;
            if (pct >= 0.1m)
            {
                warnings.Add($"Note: {longLead} appointments ({pct:P0}) were scheduled >30 days ago - recommend confirmation calls for these long-lead appointments.");
            }
        }

        return warnings;
    }

    [GeneratedRegex(@"next\s+(\d+)\s+days?")]
    private static partial Regex NextNDaysRegex();
}

/// <summary>
/// Result of parsing a date expression.
/// </summary>
public record DateParseResult(DateOnly StartDate, DateOnly EndDate, string InterpretedAs)
{
    public bool NeedsClarification { get; init; }
    public string? ClarificationMessage { get; init; }
}

/// <summary>
/// Result of appointment query including data quality and validation warnings.
/// Returns a summary for LLM processing efficiency, not full appointment objects.
/// </summary>
public record AppointmentQueryResult
{
    public int TotalCount { get; init; }
    public string DateRange { get; init; } = string.Empty;
    public List<string> Warnings { get; init; } = [];
    public string? InterpretedDateExpression { get; init; }

    /// <summary>Summary of appointments by provider (top 10)</summary>
    public List<ProviderSummary> ByProvider { get; init; } = [];

    /// <summary>Summary of appointments by department (top 10)</summary>
    public List<DepartmentSummary> ByDepartment { get; init; } = [];

    /// <summary>Summary of appointment types</summary>
    public List<TypeSummary> ByType { get; init; } = [];

    /// <summary>Time slot distribution</summary>
    public Dictionary<string, int> ByTimeSlot { get; init; } = [];

    /// <summary>New vs established patient breakdown</summary>
    public Dictionary<string, int> ByPatientStatus { get; init; } = [];

    /// <summary>List of first 5 appointment IDs for detailed lookup if needed</summary>
    public List<int> SampleAppointmentIds { get; init; } = [];

    public bool HasWarnings => Warnings.Count > 0;
}

public record ProviderSummary
{
    public int ProviderId { get; init; }
    public string ProviderName { get; init; } = string.Empty;
    public int AppointmentCount { get; init; }
}

public record DepartmentSummary
{
    public int DepartmentId { get; init; }
    public string DepartmentName { get; init; } = string.Empty;
    public int AppointmentCount { get; init; }
}

public record TypeSummary
{
    public string TypeName { get; init; } = string.Empty;
    public int Count { get; init; }
}

/// <summary>
/// Patient no-show statistics result.
/// </summary>
public record PatientNoShowStats
{
    public int PatientId { get; init; }
    public int TotalAppointments { get; init; }
    public int NoShowCount { get; init; }
    public decimal NoShowRate { get; init; }

    public string RiskCategory => NoShowRate switch
    {
        > 0.5m => "High Risk",
        > 0.2m => "Moderate Risk",
        _ => "Low Risk"
    };
}

/// <summary>
/// Daily forecast aggregation with anomaly detection.
/// </summary>
public record DailyForecast
{
    public DateOnly Date { get; init; }
    public string DayOfWeek { get; init; } = string.Empty;
    public int TotalAppointments { get; init; }
    public int PredictedNoShows { get; set; }
    public decimal PredictedNoShowRate { get; set; }
    public bool IsHighRiskDay => PredictedNoShowRate > 0.3m || RiskFactors.Count > 2;
    public bool IsAnomaly { get; set; }
    public string? AnomalyReason { get; set; }
    public List<string> RiskFactors { get; init; } = [];
}

/// <summary>
/// Complete weekly forecast result with summary and analysis.
/// </summary>
public record WeeklyForecastResult
{
    public List<DailyForecast> Forecasts { get; init; } = [];
    public int TotalAppointments { get; init; }
    public decimal AveragePerDay { get; init; }
    public List<DailyForecast> HighRiskDays { get; init; } = [];
    public string Summary { get; init; } = string.Empty;
}
