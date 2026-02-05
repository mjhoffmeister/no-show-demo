namespace NoShowPredictor.Agent.Models;

/// <summary>
/// Represents a healthcare provider.
/// </summary>
public record Provider
{
    public int ProviderId { get; init; }
    public int? ProProviderIdSource { get; init; }
    public string ProviderFirstName { get; init; } = string.Empty;
    public string ProviderLastName { get; init; } = string.Empty;
    public string ProviderType { get; init; } = string.Empty;
    public string? ProviderTypeCategory { get; init; }
    public string ProviderSpecialty { get; init; } = string.Empty;
    public string? ProviderSpecialtyServiceLine { get; init; }
    public string? ProviderNpiNumber { get; init; }
    public string? ProviderAffiliation { get; init; }
    public string? EntityType { get; init; }
    public string? BillableYn { get; init; }
    public string? PatientFacingName { get; init; }

    /// <summary>
    /// Computed display name
    /// </summary>
    public string DisplayName => $"{ProviderFirstName} {ProviderLastName}, {ProviderSpecialty}";
}
