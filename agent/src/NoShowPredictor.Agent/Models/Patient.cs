namespace NoShowPredictor.Agent.Models;

/// <summary>
/// Represents a patient with demographic attributes.
/// Maps to dbo.Patients table in database.
/// </summary>
public sealed record Patient
{
    /// <summary>Unique patient identifier (PK)</summary>
    public required int PatientId { get; init; }

    /// <summary>MRN equivalent in Epic</summary>
    public required int EnterpriseId { get; init; }

    /// <summary>Patient gender: M, F, Other</summary>
    public required string PatientGender { get; init; }

    /// <summary>Age range: 0-17, 18-39, 40-64, 65+</summary>
    public required string PatientAgeBucket { get; init; }

    /// <summary>Race/ethnicity (optional)</summary>
    public string? PatientRaceEthnicity { get; init; }

    /// <summary>Contact email (optional)</summary>
    public string? PatientEmail { get; init; }

    /// <summary>5-digit zip code (optional)</summary>
    public string? PatientZipCode { get; init; }

    /// <summary>Portal enterprise ID (optional)</summary>
    public long? PortalEnterpriseId { get; init; }

    /// <summary>Last patient portal login (optional)</summary>
    public DateTime? PortalLastLogin { get; init; }

    /// <summary>Whether patient has logged into portal within 90 days</summary>
    public bool PortalEngaged => PortalLastLogin.HasValue 
        && (DateTime.UtcNow - PortalLastLogin.Value).TotalDays <= 90;
}

/// <summary>
/// Patient gender enumeration values.
/// </summary>
public static class PatientGenders
{
    public const string Male = "M";
    public const string Female = "F";
    public const string Other = "Other";
}

/// <summary>
/// Patient age bucket enumeration values.
/// </summary>
public static class PatientAgeBuckets
{
    public const string Pediatric = "0-17";
    public const string YoungAdult = "18-39";
    public const string MiddleAged = "40-64";
    public const string Senior = "65+";
}
