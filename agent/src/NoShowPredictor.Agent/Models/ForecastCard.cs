using System.Text.Json.Serialization;

namespace NoShowPredictor.Agent.Models;

/// <summary>
/// Structured forecast card data for rich frontend visualization.
/// The agent returns this as JSON for the frontend to render as a card.
/// </summary>
public record ForecastCard
{
    /// <summary>Type discriminator for frontend parsing</summary>
    [JsonPropertyName("$type")]
    public string Type => "weeklyForecast";

    /// <summary>Friendly date range display (e.g., "Feb 5 - Feb 9")</summary>
    public string DateRange { get; init; } = string.Empty;

    /// <summary>Total appointments in the forecast period</summary>
    public int TotalAppointments { get; init; }

    /// <summary>Summary statistics</summary>
    public ForecastSummary Summary { get; init; } = new();

    /// <summary>Day-by-day breakdown</summary>
    public List<ForecastDay> Days { get; init; } = [];

    /// <summary>The day with highest risk</summary>
    public PeakDayInfo? PeakDay { get; init; }

    /// <summary>Prioritized recommendations</summary>
    public List<ForecastRecommendation> Recommendations { get; init; } = [];

    /// <summary>Data source indicator</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Whether predictions are from ML model</summary>
    public bool IsMLBased { get; init; }

    /// <summary>Optional warning message</summary>
    public string? Warning { get; init; }
}

/// <summary>
/// Summary statistics for the forecast period.
/// </summary>
public record ForecastSummary
{
    /// <summary>Count of high-risk appointments</summary>
    public int HighRisk { get; init; }

    /// <summary>Count of low-risk appointments</summary>
    public int LowRisk { get; init; }

    /// <summary>Expected number of no-shows</summary>
    public int ExpectedNoShows { get; init; }

    /// <summary>Percentage of appointments that are high-risk</summary>
    public double HighRiskPercentage { get; init; }
}

/// <summary>
/// Single day in the forecast.
/// </summary>
public record ForecastDay
{
    /// <summary>ISO date string (yyyy-MM-dd)</summary>
    public string Date { get; init; } = string.Empty;

    /// <summary>Display label (e.g., "Thu, Feb 5")</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Day of week name</summary>
    public string DayOfWeek { get; init; } = string.Empty;

    /// <summary>Total appointments this day</summary>
    public int Total { get; init; }

    /// <summary>High-risk appointments</summary>
    public int High { get; init; }

    /// <summary>Low-risk appointments</summary>
    public int Low { get; init; }

    /// <summary>Percentage of day's appointments that are high-risk</summary>
    public double HighRiskPercentage { get; init; }

    /// <summary>Whether this is the peak risk day</summary>
    public bool IsPeak { get; init; }
}

/// <summary>
/// Information about the highest-risk day.
/// </summary>
public record PeakDayInfo
{
    /// <summary>ISO date string</summary>
    public string Date { get; init; } = string.Empty;

    /// <summary>Day of week display name</summary>
    public string DayOfWeek { get; init; } = string.Empty;

    /// <summary>Friendly label</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Number of high-risk appointments</summary>
    public int HighRiskCount { get; init; }

    /// <summary>Percentage of the day that is high-risk</summary>
    public double HighRiskPercentage { get; init; }
}

/// <summary>
/// A recommendation action for the forecast.
/// </summary>
public record ForecastRecommendation
{
    /// <summary>Priority level: high, medium, low</summary>
    public string Priority { get; init; } = "medium";

    /// <summary>Recommendation icon type: call, overbook, reminder</summary>
    public string Icon { get; init; } = "reminder";

    /// <summary>Short action title</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>Target of the action</summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>Additional detail/emphasis</summary>
    public string? Detail { get; init; }
}
