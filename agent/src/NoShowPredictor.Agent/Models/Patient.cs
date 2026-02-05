namespace NoShowPredictor.Agent.Models;

/// <summary>
/// Represents a patient with demographic and behavioral attributes.
/// </summary>
public record Patient
{
    public int PatientId { get; init; }
    public int? EnterpriseId { get; init; }
    public string PatientGender { get; init; } = string.Empty;
    public string PatientAgeBucket { get; init; } = string.Empty;
    public string? PatientRaceEthnicity { get; init; }
    public string? PatientEmail { get; init; }
    public string? PatientZipCode { get; init; }
    public long? PortalEnterpriseId { get; init; }
    public DateTime? PortalLastLogin { get; init; }

    /// <summary>
    /// Historical no-show rate for this patient (0.0 to 1.0).
    /// Calculated from past appointment attendance.
    /// </summary>
    public decimal? HistoricalNoShowRate { get; init; }

    /// <summary>
    /// Total number of historical no-shows for this patient.
    /// </summary>
    public int? HistoricalNoShowCount { get; init; }

    /// <summary>
    /// Computed: portal_last_login within last 90 days
    /// </summary>
    public bool PortalEngaged => PortalLastLogin.HasValue &&
        (DateTime.UtcNow - PortalLastLogin.Value).TotalDays <= 90;
}
