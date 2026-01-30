namespace NoShowPredictor.Agent.Models;

/// <summary>
/// Represents a risk factor contributing to no-show probability.
/// </summary>
public sealed record RiskFactor
{
    /// <summary>Name of the contributing factor</summary>
    public required string Name { get; init; }

    /// <summary>Impact on prediction (-1.0 to 1.0)</summary>
    public required double Contribution { get; init; }

    /// <summary>Human-readable description</summary>
    public required string Description { get; init; }
}

/// <summary>
/// Represents a no-show prediction from the ML model.
/// Maps to dbo.Predictions table in database.
/// </summary>
public sealed record Prediction
{
    /// <summary>Unique prediction identifier (PK)</summary>
    public required Guid PredictionId { get; init; }

    /// <summary>Associated appointment (FK)</summary>
    public required int AppointmentId { get; init; }

    /// <summary>No-show probability (0.0 to 1.0)</summary>
    public required double NoShowProbability { get; init; }

    /// <summary>Categorized risk level from model</summary>
    public required string RiskLevel { get; init; }

    /// <summary>Contributing risk factors (JSON)</summary>
    public IReadOnlyList<RiskFactor> RiskFactors { get; init; } = [];

    /// <summary>Model version used for prediction</summary>
    public required string ModelVersion { get; init; }

    /// <summary>When prediction was generated</summary>
    public required DateTime PredictedAt { get; init; }

    // Navigation property
    public Appointment? Appointment { get; init; }
}

/// <summary>
/// Risk level enumeration values.
/// </summary>
public static class RiskLevels
{
    public const string Low = "Low";
    public const string Medium = "Medium";
    public const string High = "High";
}
