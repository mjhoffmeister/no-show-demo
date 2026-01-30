namespace NoShowPredictor.Agent.Models;

/// <summary>
/// Represents an agent-generated recommendation for addressing a high-risk appointment.
/// </summary>
public sealed record Recommendation
{
    /// <summary>Unique recommendation identifier (PK)</summary>
    public required int RecommendationId { get; init; }

    /// <summary>Associated prediction (FK)</summary>
    public required int PredictionId { get; init; }

    /// <summary>Recommended action type</summary>
    public required string ActionType { get; init; }

    /// <summary>Priority level (1=highest, 4=lowest)</summary>
    public required int Priority { get; init; }

    /// <summary>Human-readable description of recommendation</summary>
    public required string Description { get; init; }

    /// <summary>Rationale based on patient context</summary>
    public required string Rationale { get; init; }

    /// <summary>When recommendation was generated</summary>
    public required DateTime GeneratedAt { get; init; }

    /// <summary>Whether action was taken on this recommendation</summary>
    public bool? ActionTaken { get; init; }

    /// <summary>When action was taken (optional)</summary>
    public DateTime? ActionTakenAt { get; init; }

    /// <summary>Notes on action outcome (optional)</summary>
    public string? ActionNotes { get; init; }

    // Navigation property
    public Prediction? Prediction { get; init; }
}

/// <summary>
/// Recommendation action types.
/// </summary>
public static class ActionTypes
{
    public const string PhoneReminder = "Phone Reminder";
    public const string TextReminder = "Text Reminder";
    public const string EmailReminder = "Email Reminder";
    public const string TransportAssistance = "Transport Assistance";
    public const string Reschedule = "Reschedule";
    public const string PortalOutreach = "Portal Outreach";
    public const string CareCoordination = "Care Coordination";
    public const string Overbooking = "Overbooking Allowance";
}

/// <summary>
/// Recommendation priority levels.
/// </summary>
public static class Priorities
{
    public const int Urgent = 1;
    public const int High = 2;
    public const int Medium = 3;
    public const int Low = 4;
}
