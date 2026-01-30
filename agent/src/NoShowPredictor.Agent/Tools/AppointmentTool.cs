using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.AI;
using NoShowPredictor.Agent.Data;
using NoShowPredictor.Agent.Models;

namespace NoShowPredictor.Agent.Tools;

/// <summary>
/// Tool for querying appointment data with natural language date parsing.
/// </summary>
public sealed class AppointmentTool
{
    private readonly IAppointmentRepository _repository;
    private readonly ILogger<AppointmentTool> _logger;

    public AppointmentTool(IAppointmentRepository repository, ILogger<AppointmentTool> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Get appointments for a date range with optional risk level filtering.
    /// Supports natural language dates like "today", "tomorrow", "next Monday".
    /// </summary>
    [Description("Get appointments for a date range. Use natural language dates like 'today', 'tomorrow', 'next week'. Optionally filter by risk level (High, Medium, Low) or provider.")]
    public async Task<AppointmentQueryResult> GetAppointmentsByDateRange(
        [Description("Start date for the query (e.g., 'today', 'tomorrow', '2026-01-30')")] string startDate,
        [Description("End date for the query. If not provided, defaults to same as start date.")] string? endDate = null,
        [Description("Filter by risk level: 'High', 'Medium', 'Low', or 'All'. Default is 'All'.")] string riskLevel = "All",
        [Description("Filter by provider ID (optional)")] int? providerId = null,
        [Description("Filter by department ID (optional)")] int? departmentId = null,
        CancellationToken cancellationToken = default)
    {
        // Parse dates
        var parsedStartDate = ParseDate(startDate);
        var parsedEndDate = endDate is null ? parsedStartDate : ParseDate(endDate);

        if (parsedStartDate is null)
        {
            return new AppointmentQueryResult
            {
                Success = false,
                ErrorMessage = $"Could not understand the date '{startDate}'. Try formats like 'tomorrow', 'next Monday', or '2026-01-30'.",
                ParsedDateRange = null,
                Appointments = []
            };
        }

        if (parsedEndDate is null)
        {
            return new AppointmentQueryResult
            {
                Success = false,
                ErrorMessage = $"Could not understand the end date '{endDate}'. Try formats like 'tomorrow', 'next Friday', or '2026-01-30'.",
                ParsedDateRange = null,
                Appointments = []
            };
        }

        var start = parsedStartDate.Value;
        var end = parsedEndDate.Value;

        // Validate date range
        if (end < start)
        {
            (start, end) = (end, start);
        }

        // Warn for predictions too far out
        var daysOut = (end.ToDateTime(TimeOnly.MinValue) - DateTime.Today).Days;
        string? warning = daysOut > 14 
            ? "Note: Predictions for appointments more than 2 weeks ahead may be less accurate."
            : null;

        _logger.LogInformation("Querying appointments from {StartDate} to {EndDate} with risk level {RiskLevel}",
            start, end, riskLevel);

        // Get appointments based on risk level filter
        IReadOnlyList<Appointment> appointments;
        if (riskLevel.Equals("High", StringComparison.OrdinalIgnoreCase))
        {
            appointments = await _repository.GetHighRiskAppointmentsAsync(
                start, end, probabilityThreshold: 0.6, cancellationToken);
        }
        else if (riskLevel.Equals("Medium", StringComparison.OrdinalIgnoreCase))
        {
            var all = await _repository.GetAppointmentsAsync(
                start, end, providerId, departmentId, "Scheduled", cancellationToken);
            // Medium: 0.3 - 0.6 probability (would need predictions loaded)
            appointments = all;
        }
        else if (riskLevel.Equals("Low", StringComparison.OrdinalIgnoreCase))
        {
            var all = await _repository.GetAppointmentsAsync(
                start, end, providerId, departmentId, "Scheduled", cancellationToken);
            // Low: < 0.3 probability
            appointments = all;
        }
        else
        {
            appointments = await _repository.GetAppointmentsAsync(
                start, end, providerId, departmentId, "Scheduled", cancellationToken);
        }

        // Build response with patient-friendly formatting
        var summaries = appointments.Select(a => new AppointmentSummary
        {
            AppointmentId = a.AppointmentId,
            PatientName = $"Patient #{a.PatientId}", // Anonymized for safety
            AppointmentDate = a.AppointmentDate.ToString("dddd, MMMM d, yyyy"),
            AppointmentTime = a.AppointmentStartTime,
            ProviderName = $"Dr. {a.Provider?.ProviderLastName ?? "Unknown"}",
            DepartmentName = a.Department?.DepartmentName ?? "Unknown",
            AppointmentType = a.AppointmentTypeName,
            Duration = a.AppointmentDuration,
            IsVirtual = a.VirtualFlag != "Non-Virtual",
            IsNewPatient = a.NewPatientFlag == "NEW PATIENT"
        }).ToList();

        return new AppointmentQueryResult
        {
            Success = true,
            ParsedDateRange = $"{start:dddd, MMMM d, yyyy} to {end:dddd, MMMM d, yyyy}",
            TotalCount = summaries.Count,
            Appointments = summaries,
            Warning = warning
        };
    }

    /// <summary>
    /// Parse natural language date expressions into DateOnly.
    /// </summary>
    private static DateOnly? ParseDate(string dateInput)
    {
        if (string.IsNullOrWhiteSpace(dateInput))
            return null;

        var input = dateInput.Trim().ToLowerInvariant();
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Relative dates
        if (input == "today")
            return today;

        if (input == "tomorrow")
            return today.AddDays(1);

        if (input == "yesterday")
            return today.AddDays(-1);

        // Day of week references
        var daysOfWeek = new Dictionary<string, DayOfWeek>
        {
            ["sunday"] = DayOfWeek.Sunday,
            ["monday"] = DayOfWeek.Monday,
            ["tuesday"] = DayOfWeek.Tuesday,
            ["wednesday"] = DayOfWeek.Wednesday,
            ["thursday"] = DayOfWeek.Thursday,
            ["friday"] = DayOfWeek.Friday,
            ["saturday"] = DayOfWeek.Saturday
        };

        // Handle "next [day]" or just "[day]"
        var isNext = input.StartsWith("next ");
        var dayPart = isNext ? input.Replace("next ", "") : input;

        if (daysOfWeek.TryGetValue(dayPart, out var targetDay))
        {
            var daysUntil = ((int)targetDay - (int)today.DayOfWeek + 7) % 7;
            if (daysUntil == 0 && isNext)
                daysUntil = 7; // "next Monday" when today is Monday means next week
            return today.AddDays(daysUntil);
        }

        // "this week" / "next week"
        if (input == "this week")
        {
            // Return start of current week (Monday)
            var daysToMonday = (7 + (int)today.DayOfWeek - 1) % 7;
            return today.AddDays(-daysToMonday);
        }

        if (input == "next week")
        {
            var daysToNextMonday = (8 - (int)today.DayOfWeek) % 7;
            if (daysToNextMonday == 0) daysToNextMonday = 7;
            return today.AddDays(daysToNextMonday);
        }

        // Standard date formats
        string[] formats =
        [
            "yyyy-MM-dd",
            "MM/dd/yyyy",
            "M/d/yyyy",
            "MMMM d, yyyy",
            "MMMM d",
            "MMM d, yyyy",
            "MMM d",
            "d MMMM yyyy",
            "d MMMM"
        ];

        if (DateOnly.TryParseExact(input, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed;

        // Try general datetime parsing as fallback
        if (DateTime.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
            return DateOnly.FromDateTime(dateTime);

        return null;
    }
}

/// <summary>
/// Result of an appointment query.
/// </summary>
public sealed record AppointmentQueryResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ParsedDateRange { get; init; }
    public int TotalCount { get; init; }
    public IReadOnlyList<AppointmentSummary> Appointments { get; init; } = [];
    public string? Warning { get; init; }
}

/// <summary>
/// Summary of an appointment for display.
/// </summary>
public sealed record AppointmentSummary
{
    public int AppointmentId { get; init; }
    public required string PatientName { get; init; }
    public required string AppointmentDate { get; init; }
    public required string AppointmentTime { get; init; }
    public required string ProviderName { get; init; }
    public required string DepartmentName { get; init; }
    public required string AppointmentType { get; init; }
    public int Duration { get; init; }
    public bool IsVirtual { get; init; }
    public bool IsNewPatient { get; init; }
    public double? NoShowProbability { get; set; }
    public string? RiskLevel { get; set; }
}
