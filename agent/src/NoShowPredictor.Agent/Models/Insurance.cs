namespace NoShowPredictor.Agent.Models;

/// <summary>
/// Represents insurance/payer information for an appointment.
/// Maps to insurance table in database.
/// </summary>
public sealed record Insurance
{
    /// <summary>Unique insurance record identifier (PK)</summary>
    public required int InsuranceId { get; init; }

    /// <summary>Associated appointment (FK)</summary>
    public required int AppointmentId { get; init; }

    /// <summary>Insurance plan name</summary>
    public required string InsurancePlanName { get; init; }

    /// <summary>Payer category grouping</summary>
    public required string PayerGrouping { get; init; }

    /// <summary>Financial class code</summary>
    public required string FinancialClass { get; init; }

    /// <summary>Whether this is primary coverage</summary>
    public bool? PrimaryCoverageYN { get; init; }
}

/// <summary>
/// Payer grouping enumeration values.
/// </summary>
public static class PayerGroupings
{
    public const string Commercial = "Commercial";
    public const string Medicare = "Medicare";
    public const string Medicaid = "Medicaid";
    public const string MedicaidManagedCare = "Medicaid Managed Care";
    public const string MedicareManagedCare = "Medicare Managed Care";
    public const string SelfPay = "Self-Pay";
    public const string WorkersComp = "Workers Comp";
    public const string Other = "Other";
}
