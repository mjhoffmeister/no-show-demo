namespace NoShowPredictor.Agent.Models;

/// <summary>
/// Represents a healthcare provider.
/// Maps to providers table in database.
/// </summary>
public sealed record Provider
{
    /// <summary>Unique provider identifier (PK)</summary>
    public required int ProviderId { get; init; }

    /// <summary>Source system provider ID (optional)</summary>
    public int? ProProviderId { get; init; }

    /// <summary>Provider first name</summary>
    public required string ProviderFirstName { get; init; }

    /// <summary>Provider last name</summary>
    public required string ProviderLastName { get; init; }

    /// <summary>Type: Physician, NP, PA, etc.</summary>
    public required string ProviderType { get; init; }

    /// <summary>Provider type category (optional)</summary>
    public string? ProviderTypeCategory { get; init; }

    /// <summary>Medical specialty</summary>
    public required string ProviderSpecialty { get; init; }

    /// <summary>Service line mapping (optional)</summary>
    public string? ProviderSpecialtyServiceLine { get; init; }

    /// <summary>NPI number (optional)</summary>
    public string? ProviderNpiNumber { get; init; }

    /// <summary>Employed, Affiliated, etc. (optional)</summary>
    public string? ProviderAffiliation { get; init; }

    /// <summary>Provider entity type (optional)</summary>
    public string? EntityType { get; init; }

    /// <summary>Billable flag Y/N (optional)</summary>
    public string? BillableYN { get; init; }

    /// <summary>Patient-facing display name (optional)</summary>
    public string? PatientFacingName { get; init; }

    /// <summary>Computed display name: FirstName LastName, Specialty</summary>
    public string DisplayName => $"{ProviderFirstName} {ProviderLastName}, {ProviderSpecialty}";
}

/// <summary>
/// Provider type enumeration values.
/// </summary>
public static class ProviderTypes
{
    public const string Physician = "Physician";
    public const string NursePractitioner = "NP";
    public const string PhysicianAssistant = "PA";
    public const string RegisteredNurse = "RN";
    public const string MedicalAssistant = "MA";
}
