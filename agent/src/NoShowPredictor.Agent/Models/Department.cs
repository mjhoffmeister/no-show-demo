namespace NoShowPredictor.Agent.Models;

/// <summary>
/// Represents a clinic/department location.
/// </summary>
public record Department
{
    public int DepartmentId { get; init; }
    public string DepartmentName { get; init; } = string.Empty;
    public string? DepartmentSpecialty { get; init; }
    public string? BillingName { get; init; }
    public string? PlaceOfServiceCode { get; init; }
    public string? PlaceOfServiceType { get; init; }
    public int? ProviderGroupId { get; init; }
    public string? DepartmentGroup { get; init; }
    public int? ContextId { get; init; }
    public string? ContextName { get; init; }
    public string? Market { get; init; }
    public string? Division { get; init; }
    public string? BusinessUnit { get; init; }
}
