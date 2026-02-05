using System.ComponentModel;
using NoShowPredictor.Agent.Data;
using NoShowPredictor.Agent.Models;

namespace NoShowPredictor.Agent.Tools;

/// <summary>
/// Tools for patient-specific queries including history and risk factor analysis.
/// </summary>
public sealed class PatientTool
{
    private readonly AppointmentRepository _repository;

    public PatientTool(AppointmentRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Get comprehensive patient history including appointment records, no-show statistics, and risk factors.
    /// </summary>
    [Description("Get a patient's complete appointment history, attendance statistics, and risk factor analysis. Use after finding a patient via SearchPatients.")]
    public async Task<PatientHistoryResult> GetPatientHistory(
        [Description("The patient ID to retrieve history for")] int patientId,
        CancellationToken cancellationToken = default)
    {
        // Get all past appointments for the patient
        var allAppointments = await _repository.GetAppointmentsByPatientAsync(patientId, cancellationToken);

        // Separate past and future appointments
        var now = DateTime.UtcNow;
        var pastAppointments = allAppointments
            .Where(a => a.AppointmentDateTime < now)
            .OrderByDescending(a => a.AppointmentDateTime)
            .ToList();

        var upcomingAppointments = allAppointments
            .Where(a => a.AppointmentDateTime >= now)
            .OrderBy(a => a.AppointmentDateTime)
            .ToList();

        // Calculate statistics
        var (totalAppointments, noShowCount, noShowRate) = await _repository.GetPatientNoShowStatsAsync(patientId, cancellationToken);

        // Determine if this is a new patient (limited history)
        var isNewPatient = totalAppointments < 3;

        // Get patient details from most recent appointment
        var patient = pastAppointments.FirstOrDefault()?.Patient ?? upcomingAppointments.FirstOrDefault()?.Patient;

        // Generate risk factors based on history
        var riskFactors = AnalyzePatientRiskFactors(pastAppointments, patient, noShowRate);

        // Get attendance patterns
        var attendancePatterns = AnalyzeAttendancePatterns(pastAppointments);

        return new PatientHistoryResult
        {
            PatientId = patientId,
            PatientEmail = patient?.PatientEmail,
            PatientAgeBucket = patient?.PatientAgeBucket,
            PatientGender = patient?.PatientGender,
            PortalEngaged = patient?.PortalEngaged ?? false,
            IsNewPatient = isNewPatient,
            TotalPastAppointments = totalAppointments,
            NoShowCount = noShowCount,
            NoShowRate = noShowRate,
            RiskLevel = ClassifyRiskLevel(noShowRate, noShowCount, isNewPatient),
            RiskFactors = riskFactors,
            AttendancePatterns = attendancePatterns,
            RecentAppointments = pastAppointments.Take(5).Select(MapToAppointmentSummary).ToList(),
            UpcomingAppointments = upcomingAppointments.Select(MapToAppointmentSummary).ToList(),
            Summary = GeneratePatientSummary(patient, totalAppointments, noShowCount, noShowRate, isNewPatient, riskFactors)
        };
    }

    /// <summary>
    /// Get risk factor explanation for a specific patient.
    /// </summary>
    [Description("Explain what factors contribute to a patient's no-show risk. Use to understand why a patient has high risk.")]
    public async Task<PatientRiskExplanation> GetPatientRiskExplanation(
        [Description("The patient ID to analyze")] int patientId,
        CancellationToken cancellationToken = default)
    {
        var history = await GetPatientHistory(patientId, cancellationToken);

        return new PatientRiskExplanation
        {
            PatientId = patientId,
            OverallRisk = history.RiskLevel,
            RiskFactors = history.RiskFactors,
            MitigationSuggestions = GenerateMitigationSuggestions(history),
            PatternInsights = GeneratePatternInsights(history.AttendancePatterns),
            RecommendedActions = GenerateRecommendedActions(history)
        };
    }

    /// <summary>
    /// Compare a patient's no-show behavior to clinic averages.
    /// </summary>
    [Description("Compare a patient's attendance pattern to clinic-wide averages to provide context.")]
    public async Task<PatientComparisonResult> CompareToClinicAverages(
        [Description("The patient ID to compare")] int patientId,
        CancellationToken cancellationToken = default)
    {
        var history = await GetPatientHistory(patientId, cancellationToken);

        // Clinic averages (could be calculated from DB, using reasonable defaults)
        const decimal clinicAverageNoShowRate = 0.18m; // 18% average
        const int clinicAverageAppointmentsPerYear = 4;

        var comparison = history.NoShowRate - clinicAverageNoShowRate;
        var appointmentFrequency = history.TotalPastAppointments > 0
            ? (decimal)history.TotalPastAppointments / Math.Max(1, GetYearsOfHistory(history.RecentAppointments))
            : 0;

        return new PatientComparisonResult
        {
            PatientId = patientId,
            PatientNoShowRate = history.NoShowRate,
            ClinicAverageNoShowRate = clinicAverageNoShowRate,
            Comparison = comparison switch
            {
                > 0.1m => "Significantly higher than clinic average",
                > 0.05m => "Moderately higher than clinic average",
                > -0.05m => "Similar to clinic average",
                > -0.1m => "Better than clinic average",
                _ => "Significantly better than clinic average"
            },
            PatientAppointmentsPerYear = appointmentFrequency,
            ClinicAverageAppointmentsPerYear = clinicAverageAppointmentsPerYear,
            EngagementLevel = appointmentFrequency >= clinicAverageAppointmentsPerYear
                ? "High engagement"
                : "Below average engagement"
        };
    }

    private static List<string> AnalyzePatientRiskFactors(
        List<Appointment> pastAppointments,
        Patient? patient,
        decimal noShowRate)
    {
        var factors = new List<string>();

        // Historical no-show pattern
        if (noShowRate >= 0.4m)
        {
            factors.Add($"High historical no-show rate ({noShowRate:P0}) - established pattern of missed appointments");
        }
        else if (noShowRate >= 0.25m)
        {
            factors.Add($"Moderate historical no-show rate ({noShowRate:P0}) - some attendance concerns");
        }

        // Portal engagement
        if (patient != null && !patient.PortalEngaged)
        {
            factors.Add("Not engaged with patient portal - may miss reminders and communications");
        }

        // Insurance type patterns
        var insuranceTypes = pastAppointments
            .Where(a => a.Insurance?.Sipg1 != null)
            .GroupBy(a => a.Insurance!.Sipg1)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        if (insuranceTypes?.Key is "Medicaid" or "Self Pay")
        {
            factors.Add($"Primary insurance type ({insuranceTypes.Key}) associated with higher no-show rates");
        }

        // Day of week patterns
        var noShowsByDay = pastAppointments
            .Where(a => a.AppointmentStatus == "No Show")
            .GroupBy(a => a.AppointmentDate.DayOfWeek)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        if (noShowsByDay != null && noShowsByDay.Count() >= 2)
        {
            factors.Add($"Pattern of no-shows on {noShowsByDay.Key}s ({noShowsByDay.Count()} missed)");
        }

        // Lead time patterns
        var avgLeadTimeMissed = pastAppointments
            .Where(a => a.AppointmentStatus == "No Show")
            .Select(a => a.LeadTimeDays)
            .DefaultIfEmpty(0)
            .Average();

        var avgLeadTimeAttended = pastAppointments
            .Where(a => a.AppointmentStatus is "Checked In" or "Completed")
            .Select(a => a.LeadTimeDays)
            .DefaultIfEmpty(0)
            .Average();

        if (avgLeadTimeMissed > avgLeadTimeAttended + 7)
        {
            factors.Add($"Higher no-show rate with longer lead times (avg {avgLeadTimeMissed:F0} days for missed vs {avgLeadTimeAttended:F0} for attended)");
        }

        // Time of day patterns
        var afternoonNoShows = pastAppointments
            .Where(a => a.AppointmentStatus == "No Show" && a.HourOfDay >= 14)
            .Count();
        var totalAfternoon = pastAppointments.Count(a => a.HourOfDay >= 14);

        if (totalAfternoon >= 3 && afternoonNoShows > totalAfternoon * 0.3)
        {
            factors.Add("Higher no-show rate for afternoon appointments");
        }

        // Age-related patterns (from bucket)
        if (patient?.PatientAgeBucket is "18-25" or "26-35")
        {
            factors.Add($"Age group ({patient.PatientAgeBucket}) has statistically higher no-show rates");
        }

        // New patient status
        if (pastAppointments.Count < 3)
        {
            factors.Add("Limited appointment history - insufficient data for reliable prediction");
        }

        // Recent trend
        var recentAppointments = pastAppointments.Take(5).ToList();
        var recentNoShows = recentAppointments.Count(a => a.AppointmentStatus == "No Show");
        if (recentAppointments.Count >= 3 && recentNoShows >= 2)
        {
            factors.Add("Recent trend shows increasing no-shows in last 5 appointments");
        }

        return factors;
    }

    private static AttendancePatterns AnalyzeAttendancePatterns(List<Appointment> pastAppointments)
    {
        var attended = pastAppointments.Where(a => a.AppointmentStatus is "Checked In" or "Completed").ToList();
        var noShows = pastAppointments.Where(a => a.AppointmentStatus == "No Show").ToList();
        var cancelled = pastAppointments.Where(a => a.AppointmentStatus == "Cancelled").ToList();

        // Day of week preferences (attended)
        var preferredDays = attended
            .GroupBy(a => a.AppointmentDate.DayOfWeek)
            .OrderByDescending(g => g.Count())
            .Take(2)
            .Select(g => g.Key.ToString())
            .ToList();

        // Time of day preferences
        var preferredTimes = attended
            .GroupBy(a => a.HourOfDay switch
            {
                < 10 => "Morning (before 10am)",
                < 12 => "Late Morning (10am-12pm)",
                < 14 => "Early Afternoon (12pm-2pm)",
                < 17 => "Afternoon (2pm-5pm)",
                _ => "Evening (after 5pm)"
            })
            .OrderByDescending(g => g.Count())
            .Take(2)
            .Select(g => g.Key)
            .ToList();

        // Providers with good attendance
        var preferredProviders = attended
            .GroupBy(a => a.Provider?.DisplayName ?? "Unknown")
            .Where(g => g.Count() >= 2)
            .OrderByDescending(g => g.Count())
            .Take(3)
            .Select(g => g.Key)
            .ToList();

        return new AttendancePatterns
        {
            TotalAttended = attended.Count,
            TotalNoShows = noShows.Count,
            TotalCancelled = cancelled.Count,
            PreferredDays = preferredDays,
            PreferredTimes = preferredTimes,
            PreferredProviders = preferredProviders,
            AverageLeadTimeForAttended = attended.Count > 0
                ? attended.Average(a => a.LeadTimeDays)
                : 0,
            AverageLeadTimeForNoShows = noShows.Count > 0
                ? noShows.Average(a => a.LeadTimeDays)
                : 0
        };
    }

    private static string ClassifyRiskLevel(decimal noShowRate, int noShowCount, bool isNewPatient)
    {
        if (isNewPatient)
            return "Unknown (New Patient)";

        return noShowRate switch
        {
            >= 0.4m => "High Risk",
            >= 0.25m => "Moderate Risk",
            >= 0.15m => "Low Risk",
            _ => "Very Low Risk"
        };
    }

    private static string GeneratePatientSummary(
        Patient? patient,
        int totalAppointments,
        int noShowCount,
        decimal noShowRate,
        bool isNewPatient,
        List<string> riskFactors)
    {
        if (isNewPatient)
        {
            return $"New patient with limited history ({totalAppointments} past appointments). " +
                   "Insufficient data for reliable no-show prediction. " +
                   "Recommend standard new patient protocols including confirmation calls.";
        }

        var riskLevel = noShowRate switch
        {
            >= 0.4m => "high risk",
            >= 0.25m => "moderate risk",
            >= 0.15m => "low risk",
            _ => "very reliable"
        };

        var summary = $"Patient has {totalAppointments} past appointments with {noShowCount} no-shows ({noShowRate:P0} rate). ";
        summary += $"This is a {riskLevel} patient. ";

        if (riskFactors.Count > 0)
        {
            summary += $"Key risk factors: {riskFactors.First()}";
            if (riskFactors.Count > 1)
            {
                summary += $" and {riskFactors.Count - 1} other factors.";
            }
        }

        return summary;
    }

    private static List<string> GenerateMitigationSuggestions(PatientHistoryResult history)
    {
        var suggestions = new List<string>();

        if (!history.PortalEngaged)
        {
            suggestions.Add("Encourage patient portal enrollment for automated reminders");
        }

        if (history.AttendancePatterns.AverageLeadTimeForNoShows > history.AttendancePatterns.AverageLeadTimeForAttended + 5)
        {
            suggestions.Add("Schedule appointments closer to current date when possible");
        }

        if (history.AttendancePatterns.PreferredDays.Count > 0)
        {
            suggestions.Add($"Schedule on preferred days: {string.Join(", ", history.AttendancePatterns.PreferredDays)}");
        }

        if (history.AttendancePatterns.PreferredTimes.Count > 0)
        {
            suggestions.Add($"Schedule at preferred times: {string.Join(", ", history.AttendancePatterns.PreferredTimes)}");
        }

        if (history.NoShowRate >= 0.3m)
        {
            suggestions.Add("Make confirmation call 24-48 hours before appointment");
            suggestions.Add("Consider overbooking slot to compensate for high no-show risk");
        }

        return suggestions;
    }

    private static List<string> GeneratePatternInsights(AttendancePatterns patterns)
    {
        var insights = new List<string>();

        if (patterns.PreferredDays.Count > 0)
        {
            insights.Add($"Best attendance on: {string.Join(", ", patterns.PreferredDays)}");
        }

        if (patterns.PreferredTimes.Count > 0)
        {
            insights.Add($"Preferred appointment times: {string.Join(", ", patterns.PreferredTimes)}");
        }

        if (patterns.PreferredProviders.Count > 0)
        {
            insights.Add($"Good relationship with: {string.Join(", ", patterns.PreferredProviders)}");
        }

        if (patterns.TotalCancelled > patterns.TotalNoShows)
        {
            insights.Add("Patient typically cancels rather than no-shows - responsive to communications");
        }

        return insights;
    }

    private static List<string> GenerateRecommendedActions(PatientHistoryResult history)
    {
        var actions = new List<string>();

        if (history.IsNewPatient)
        {
            actions.Add("Standard new patient confirmation protocol");
            actions.Add("Provide clear directions and parking information");
            actions.Add("Send appointment reminder 48 hours before");
            return actions;
        }

        if (history.NoShowRate >= 0.4m)
        {
            actions.Add("High Priority: Make confirmation call 24-48 hours before");
            actions.Add("Consider requiring prepayment or deposit");
            actions.Add("Flag for overbooking consideration");
        }
        else if (history.NoShowRate >= 0.25m)
        {
            actions.Add("Send text reminder day before");
            actions.Add("Follow up with confirmation call if no response");
        }
        else
        {
            actions.Add("Standard reminder sufficient");
        }

        if (history.UpcomingAppointments.Count > 0)
        {
            var nextAppt = history.UpcomingAppointments.First();
            actions.Add($"Monitor upcoming appointment on {nextAppt.Date:d}");
        }

        return actions;
    }

    private static AppointmentSummary MapToAppointmentSummary(Appointment appt)
    {
        return new AppointmentSummary
        {
            AppointmentId = appt.AppointmentId,
            Date = appt.AppointmentDate,
            Time = appt.AppointmentStartTime,
            Status = appt.AppointmentStatus,
            ProviderName = appt.Provider?.DisplayName ?? "Unknown",
            AppointmentType = appt.AppointmentTypeName,
            Department = appt.Department?.DepartmentName ?? "Unknown",
            LeadTimeDays = appt.LeadTimeDays
        };
    }

    private static decimal GetYearsOfHistory(List<AppointmentSummary> appointments)
    {
        if (appointments.Count == 0) return 1;
        var oldest = appointments.Last().Date;
        var days = (DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - oldest.DayNumber);
        return Math.Max(1, days / 365m);
    }
}

/// <summary>
/// Comprehensive patient history result.
/// </summary>
public record PatientHistoryResult
{
    public int PatientId { get; init; }
    public string? PatientEmail { get; init; }
    public string? PatientAgeBucket { get; init; }
    public string? PatientGender { get; init; }
    public bool PortalEngaged { get; init; }
    public bool IsNewPatient { get; init; }
    public int TotalPastAppointments { get; init; }
    public int NoShowCount { get; init; }
    public decimal NoShowRate { get; init; }
    public string RiskLevel { get; init; } = string.Empty;
    public List<string> RiskFactors { get; init; } = [];
    public AttendancePatterns AttendancePatterns { get; init; } = new();
    public List<AppointmentSummary> RecentAppointments { get; init; } = [];
    public List<AppointmentSummary> UpcomingAppointments { get; init; } = [];
    public string Summary { get; init; } = string.Empty;
}

/// <summary>
/// Patient attendance patterns analysis.
/// </summary>
public record AttendancePatterns
{
    public int TotalAttended { get; init; }
    public int TotalNoShows { get; init; }
    public int TotalCancelled { get; init; }
    public List<string> PreferredDays { get; init; } = [];
    public List<string> PreferredTimes { get; init; } = [];
    public List<string> PreferredProviders { get; init; } = [];
    public double AverageLeadTimeForAttended { get; init; }
    public double AverageLeadTimeForNoShows { get; init; }
}

/// <summary>
/// Brief appointment summary for history display.
/// </summary>
public record AppointmentSummary
{
    public int AppointmentId { get; init; }
    public DateOnly Date { get; init; }
    public string Time { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public string AppointmentType { get; init; } = string.Empty;
    public string Department { get; init; } = string.Empty;
    public int LeadTimeDays { get; init; }
}

/// <summary>
/// Patient risk factor explanation.
/// </summary>
public record PatientRiskExplanation
{
    public int PatientId { get; init; }
    public string OverallRisk { get; init; } = string.Empty;
    public List<string> RiskFactors { get; init; } = [];
    public List<string> MitigationSuggestions { get; init; } = [];
    public List<string> PatternInsights { get; init; } = [];
    public List<string> RecommendedActions { get; init; } = [];
}

/// <summary>
/// Patient comparison to clinic averages.
/// </summary>
public record PatientComparisonResult
{
    public int PatientId { get; init; }
    public decimal PatientNoShowRate { get; init; }
    public decimal ClinicAverageNoShowRate { get; init; }
    public string Comparison { get; init; } = string.Empty;
    public decimal PatientAppointmentsPerYear { get; init; }
    public decimal ClinicAverageAppointmentsPerYear { get; init; }
    public string EngagementLevel { get; init; } = string.Empty;
}
