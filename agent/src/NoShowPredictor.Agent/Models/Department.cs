namespace NoShowPredictor.Agent.Models;

/// <summary>
/// Represents a clinic/department location.
/// Maps to departments table in database.
/// </summary>
public sealed record Department
{
    /// <summary>Unique department identifier (PK)</summary>
    public required int DepartmentId { get; init; }

    /// <summary>Department name</summary>
    public required string DepartmentName { get; init; }

    /// <summary>Department specialty (optional)</summary>
    public string? DepartmentSpecialty { get; init; }

    /// <summary>Billing name (optional)</summary>
    public string? BillingName { get; init; }

    /// <summary>Place of service code (optional)</summary>
    public string? PlaceOfServiceCode { get; init; }

    /// <summary>Office, Telehealth, etc. (optional)</summary>
    public string? PlaceOfServiceType { get; init; }

    /// <summary>Provider group ID (optional)</summary>
    public int? ProviderGroupId { get; init; }

    /// <summary>Department group (optional)</summary>
    public string? DepartmentGroup { get; init; }

    /// <summary>Context/org ID (optional)</summary>
    public int? ContextId { get; init; }

    /// <summary>Context name (Region A, Region B) (optional)</summary>
    public string? ContextName { get; init; }

    /// <summary>Market region (optional)</summary>
    public string? Market { get; init; }

    /// <summary>Division/business group (optional)</summary>
    public string? Division { get; init; }

    /// <summary>Business unit (optional)</summary>
    public string? BusinessUnit { get; init; }
}

/// <summary>
/// Place of service type enumeration values.
/// </summary>
public static class PlaceOfServiceTypes
{
    public const string Office = "Office";
    public const string Telehealth = "Telehealth";
    public const string Hospital = "Hospital";
    public const string UrgentCare = "Urgent Care";
    public const string Other = "Other";
}
