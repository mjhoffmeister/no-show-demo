namespace NoShowPredictor.Agent.Models;

/// <summary>
/// Represents a scheduled medical appointment.
/// Maps to appointments table in database.
/// </summary>
public sealed record Appointment
{
    /// <summary>Unique appointment identifier (PK)</summary>
    public required int AppointmentId { get; init; }

    /// <summary>Parent for grouped appointments (optional)</summary>
    public int? ParentAppointmentId { get; init; }

    /// <summary>Patient reference (FK)</summary>
    public required int PatientId { get; init; }

    /// <summary>Provider reference (FK)</summary>
    public required int ProviderId { get; init; }

    /// <summary>Department reference (FK)</summary>
    public required int DepartmentId { get; init; }

    /// <summary>Referring provider (optional)</summary>
    public int? ReferringProviderId { get; init; }

    /// <summary>Scheduled date</summary>
    public required DateOnly AppointmentDate { get; init; }

    /// <summary>Start time (HH:MM format)</summary>
    public required string AppointmentStartTime { get; init; }

    /// <summary>Duration in minutes</summary>
    public required int AppointmentDuration { get; init; }

    /// <summary>Appointment type ID</summary>
    public required int AppointmentTypeId { get; init; }

    /// <summary>Appointment type name</summary>
    public required string AppointmentTypeName { get; init; }

    /// <summary>Current status: Scheduled, Checked In, Complete, Cancelled, No Show</summary>
    public required string AppointmentStatus { get; init; }

    /// <summary>When appointment was created</summary>
    public required DateTime AppointmentCreatedDateTime { get; init; }

    /// <summary>When appointment was scheduled</summary>
    public required DateTime AppointmentScheduledDateTime { get; init; }

    /// <summary>Check-in time (optional)</summary>
    public DateTime? AppointmentCheckInDateTime { get; init; }

    /// <summary>Check-out time (optional)</summary>
    public DateTime? AppointmentCheckOutDateTime { get; init; }

    /// <summary>Cancellation time (optional)</summary>
    public DateTime? AppointmentCancelledDateTime { get; init; }

    /// <summary>Online schedulable flag (0/1)</summary>
    public int? WebSchedulableYN { get; init; }

    /// <summary>Virtual-Telephone, Virtual-Video, Non-Virtual</summary>
    public required string VirtualFlag { get; init; }

    /// <summary>NEW PATIENT or EST PATIENT</summary>
    public required string NewPatientFlag { get; init; }

    /// <summary>Combined appointment date and time</summary>
    public DateTime AppointmentDateTime => 
        AppointmentDate.ToDateTime(TimeOnly.Parse(AppointmentStartTime));

    /// <summary>Days between scheduling and appointment</summary>
    public int LeadTimeDays => 
        Math.Max(0, (AppointmentDateTime - AppointmentScheduledDateTime).Days);

    /// <summary>Day of week (Monday=0, Sunday=6)</summary>
    public int DayOfWeek => (int)AppointmentDate.DayOfWeek;

    /// <summary>Hour of appointment (0-23)</summary>
    public int HourOfDay => TimeOnly.Parse(AppointmentStartTime).Hour;

    /// <summary>Whether appointment is in the past</summary>
    public bool IsPast => AppointmentDateTime < DateTime.UtcNow;

    /// <summary>Whether this was a no-show appointment</summary>
    public bool NoShow => AppointmentStatus == AppointmentStatuses.NoShow
        || (IsPast && AppointmentStatus == AppointmentStatuses.Scheduled && !AppointmentCheckInDateTime.HasValue);

    // Navigation properties (populated by repository)
    public Patient? Patient { get; init; }
    public Provider? Provider { get; init; }
    public Department? Department { get; init; }
}

/// <summary>
/// Appointment status enumeration values.
/// </summary>
public static class AppointmentStatuses
{
    public const string Scheduled = "Scheduled";
    public const string CheckedIn = "Checked In";
    public const string CheckedOut = "Checked Out";
    public const string Complete = "Complete";
    public const string Cancelled = "Cancelled";
    public const string NoShow = "No Show";
    public const string Rescheduled = "Rescheduled";
}

/// <summary>
/// Virtual flag enumeration values.
/// </summary>
public static class VirtualFlags
{
    public const string NonVirtual = "Non-Virtual";
    public const string VirtualVideo = "Virtual-Video";
    public const string VirtualTelephone = "Virtual-Telephone";
}

/// <summary>
/// New patient flag enumeration values.
/// </summary>
public static class NewPatientFlags
{
    public const string NewPatient = "NEW PATIENT";
    public const string Established = "EST PATIENT";
}
