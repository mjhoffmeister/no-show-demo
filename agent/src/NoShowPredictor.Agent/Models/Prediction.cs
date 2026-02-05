namespace NoShowPredictor.Agent.Models;

/// <summary>
/// Represents an ML model prediction for an appointment.
/// </summary>
public record Prediction
{
    public string PredictionId { get; init; } = Guid.NewGuid().ToString();
    public int AppointmentId { get; init; }

    /// <summary>
    /// Raw ML classification: true = predicted no-show, false = predicted to attend.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool PredictedNoShow { get; init; }

    /// <summary>
    /// Risk level based on ML binary classification.
    /// High = model predicts no-show, Low = model predicts attendance.
    /// </summary>
    public string RiskLevel => PredictedNoShow ? "High" : "Low";

    public List<RiskFactor> RiskFactors { get; init; } = [];
    public string ModelVersion { get; init; } = string.Empty;
    public DateTime PredictedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Represents a contributing factor to a prediction.
/// </summary>
public record RiskFactor
{
    public string FactorName { get; init; } = string.Empty;
    public string FactorValue { get; init; } = string.Empty;
    public decimal Contribution { get; init; }
    public string Direction { get; init; } = "Increases";
}
