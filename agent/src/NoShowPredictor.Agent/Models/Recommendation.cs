namespace NoShowPredictor.Agent.Models;

/// <summary>
/// Represents a suggested action based on prediction analysis.
/// </summary>
public record Recommendation
{
    public string RecommendationId { get; init; } = Guid.NewGuid().ToString();
    public int AppointmentId { get; init; }
    public ActionType ActionType { get; init; }
    public Priority Priority { get; init; }
    public string Rationale { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    // Context information for display
    public string? PatientName { get; init; }
    public string? ProviderName { get; init; }
    public DateTime? AppointmentDateTime { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    internal bool PredictedNoShow { get; init; }
}

public enum ActionType
{
    ConfirmationCall,
    Reminder,
    Overbook,
    NoAction
}

public enum Priority
{
    Urgent,
    High,
    Medium,
    Low
}
