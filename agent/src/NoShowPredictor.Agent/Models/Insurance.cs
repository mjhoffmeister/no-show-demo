namespace NoShowPredictor.Agent.Models;

/// <summary>
/// Represents patient insurance information.
/// </summary>
public record Insurance
{
    public int PrimaryPatientInsuranceId { get; init; }
    public int PatientId { get; init; }
    public string? Sipg1 { get; init; }
    public string? Sipg2 { get; init; }
    public string? InsurancePlan1CompanyDescription { get; init; }
    public string? InsuranceGroupId { get; init; }
}
