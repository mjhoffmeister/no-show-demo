namespace NoShowPredictor.Agent.Models;

/// <summary>
/// Represents a scheduled medical appointment.
/// </summary>
public record Appointment
{
    public int AppointmentId { get; init; }
    public int? ParentAppointmentId { get; init; }
    public int PatientId { get; init; }
    public int ProviderId { get; init; }
    public int DepartmentId { get; init; }
    public int? ReferringProviderId { get; init; }
    public int? ReferralAuthId { get; init; }
    public DateOnly AppointmentDate { get; init; }
    public string AppointmentStartTime { get; init; } = string.Empty;
    public DateTime AppointmentDateTime { get; init; }
    public int AppointmentDuration { get; init; }
    public int AppointmentTypeId { get; init; }
    public string AppointmentTypeName { get; init; } = string.Empty;
    public string AppointmentStatus { get; init; } = string.Empty;
    public DateTime AppointmentCreatedDateTime { get; init; }
    public string? AppointmentCreatedBy { get; init; }
    public DateTime AppointmentScheduledDateTime { get; init; }
    public string? ScheduledBy { get; init; }
    public DateTime? AppointmentCheckinDateTime { get; init; }
    public DateTime? AppointmentCheckoutDateTime { get; init; }
    public DateTime? AppointmentCancelledDateTime { get; init; }
    public string? CancelledBy { get; init; }
    public string? AppointmentCancelReason { get; init; }
    public int? RescheduledAppointmentId { get; init; }
    public DateTime? RescheduledDateTime { get; init; }
    public string? RescheduledBy { get; init; }
    public DateTime? StartCheckinDateTime { get; init; }
    public DateTime? StopSignoffDateTime { get; init; }
    public DateTime? AppointmentDeletedDateTime { get; init; }
    public int? ClaimId { get; init; }
    public decimal? CycleTime { get; init; }
    public string? FrozenYn { get; init; }
    public string? AppointmentFrozenReason { get; init; }
    public string VirtualFlag { get; init; } = "Non-Virtual";
    public string NewPatientFlag { get; init; } = "EST PATIENT";
    public int? WebSchedulableYn { get; init; }

    // Navigation properties (populated when needed)
    public Patient? Patient { get; init; }
    public Provider? Provider { get; init; }
    public Department? Department { get; init; }
    public Insurance? Insurance { get; init; }

    /// <summary>
    /// Days between scheduling and appointment
    /// </summary>
    public int LeadTimeDays => (int)(AppointmentDateTime - AppointmentScheduledDateTime).TotalDays;

    /// <summary>
    /// Day of week (0=Monday, 6=Sunday)
    /// </summary>
    public int DayOfWeek => ((int)AppointmentDate.DayOfWeek + 6) % 7;

    /// <summary>
    /// Hour of appointment (0-23)
    /// </summary>
    public int HourOfDay => AppointmentDateTime.Hour;

    /// <summary>
    /// Whether appointment is in the past
    /// </summary>
    public bool IsPast => AppointmentDateTime < DateTime.UtcNow;
}
