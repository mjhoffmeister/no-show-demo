using System.ComponentModel;
using NoShowPredictor.Agent.Data;
using NoShowPredictor.Agent.Models;
using NoShowPredictor.Agent.Services;

namespace NoShowPredictor.Agent.Tools;

/// <summary>
/// Agent tool for generating scheduling recommendations based on predictions.
/// Implements User Story 2 (T069-T072).
/// </summary>
public class RecommendationTool
{
    private readonly RecommendationService _recommendationService;
    private readonly AppointmentRepository _appointmentRepository;

    public RecommendationTool(RecommendationService recommendationService, AppointmentRepository appointmentRepository)
    {
        _recommendationService = recommendationService;
        _appointmentRepository = appointmentRepository;
    }

    /// <summary>
    /// Get scheduling recommendations for appointments based on their no-show predictions.
    /// </summary>
    /// <param name="predictions">List of appointment predictions from PredictionTool.GetPredictions</param>
    /// <returns>List of recommendations sorted by priority</returns>
    [Description("Generate scheduling recommendations (phone calls, reminders, overbooking) based on no-show predictions. Pass the predictions from GetPredictions to this method.")]
    public IReadOnlyList<Recommendation> GetRecommendations(
        [Description("Predictions from PredictionTool.GetPredictions")] IEnumerable<AppointmentPrediction> predictions)
    {
        return _recommendationService.GenerateRecommendations(predictions);
    }

    /// <summary>
    /// Get recommendations formatted for a specific date - answers "What scheduling actions should I take for tomorrow?"
    /// </summary>
    [Description("Get recommendations for a specific date. Answers questions like 'What scheduling actions should I take for tomorrow?'")]
    public RecommendationSummary GetRecommendationSummary(
        [Description("Predictions for the date")] IEnumerable<AppointmentPrediction> predictions,
        [Description("Date being analyzed")] DateOnly date)
    {
        var predictionsList = predictions.ToList();
        var recommendations = _recommendationService.GenerateRecommendations(predictionsList);
        var providerAnalysis = _recommendationService.AnalyzeProviderSchedules(predictionsList, date);

        var urgentCalls = recommendations.Where(r => r.ActionType == ActionType.ConfirmationCall && r.Priority == Priority.Urgent).ToList();
        var highPriorityCalls = recommendations.Where(r => r.ActionType == ActionType.ConfirmationCall && r.Priority == Priority.High).ToList();
        var reminders = recommendations.Where(r => r.ActionType == ActionType.Reminder).ToList();
        var overbookCandidates = recommendations.Where(r => r.ActionType == ActionType.Overbook).ToList();

        return new RecommendationSummary
        {
            Date = date,
            TotalAppointments = predictionsList.Count,
            HighRiskCount = predictionsList.Count(p => p.RiskLevel == "High"),
            MediumRiskCount = predictionsList.Count(p => p.RiskLevel == "Medium"),
            LowRiskCount = predictionsList.Count(p => p.RiskLevel == "Low"),
            UrgentConfirmationCalls = urgentCalls.Select(r => new ActionItem
            {
                PatientName = r.PatientName ?? "Unknown",
                AppointmentTime = r.AppointmentDateTime?.ToString("h:mm tt") ?? "",
                ProviderName = r.ProviderName ?? "Unknown",
                Rationale = r.Rationale,
                IsHighRisk = r.PredictedNoShow
            }).ToList(),
            HighPriorityConfirmationCalls = highPriorityCalls.Select(r => new ActionItem
            {
                PatientName = r.PatientName ?? "Unknown",
                AppointmentTime = r.AppointmentDateTime?.ToString("h:mm tt") ?? "",
                ProviderName = r.ProviderName ?? "Unknown",
                Rationale = r.Rationale,
                IsHighRisk = r.PredictedNoShow
            }).ToList(),
            ReminderActions = reminders.Select(r => new ActionItem
            {
                PatientName = r.PatientName ?? "Unknown",
                AppointmentTime = r.AppointmentDateTime?.ToString("h:mm tt") ?? "",
                ProviderName = r.ProviderName ?? "Unknown",
                Rationale = r.Rationale,
                IsHighRisk = r.PredictedNoShow
            }).ToList(),
            OverbookConsiderations = overbookCandidates.Select(r => new ActionItem
            {
                PatientName = r.PatientName ?? "Unknown",
                AppointmentTime = r.AppointmentDateTime?.ToString("h:mm tt") ?? "",
                ProviderName = r.ProviderName ?? "Unknown",
                Rationale = r.Rationale,
                IsHighRisk = r.PredictedNoShow
            }).ToList(),
            ProviderOverbookAnalysis = providerAnalysis.ToList()
        };
    }

    /// <summary>
    /// Get recommendation for a specific patient's upcoming appointment.
    /// </summary>
    [Description("Get detailed recommendation for a specific patient's appointment. Answers 'What should I do about [patient]'s appointment?'")]
    public async Task<PatientRecommendation> GetPatientRecommendation(
        [Description("The patient's appointment prediction")] AppointmentPrediction prediction,
        CancellationToken cancellationToken = default)
    {
        // Get patient's historical stats
        var stats = await _appointmentRepository.GetPatientNoShowStatsAsync(
            prediction.AppointmentId, // Note: would need patient ID passed instead
            cancellationToken);

        var patientStats = new PatientNoShowStats
        {
            PatientId = prediction.AppointmentId, // Simplified - would use actual patient ID
            TotalAppointments = stats.totalAppointments,
            NoShowCount = stats.noShowCount,
            NoShowRate = stats.noShowRate
        };

        return _recommendationService.GeneratePatientRecommendation(prediction, patientStats);
    }
}

/// <summary>
/// Summary of recommendations for a date - formatted for agent response.
/// </summary>
public record RecommendationSummary
{
    public DateOnly Date { get; init; }
    public int TotalAppointments { get; init; }
    public int HighRiskCount { get; init; }
    public int MediumRiskCount { get; init; }
    public int LowRiskCount { get; init; }
    public List<ActionItem> UrgentConfirmationCalls { get; init; } = [];
    public List<ActionItem> HighPriorityConfirmationCalls { get; init; } = [];
    public List<ActionItem> ReminderActions { get; init; } = [];
    public List<ActionItem> OverbookConsiderations { get; init; } = [];
    public List<ProviderOverbookAnalysis> ProviderOverbookAnalysis { get; init; } = [];

    public string FormattedSummary => $"""
        ## Scheduling Actions for {Date:MMMM d, yyyy}
        
        **Overview**: {TotalAppointments} appointments | {HighRiskCount} high-risk | {MediumRiskCount} medium-risk | {LowRiskCount} low-risk
        
        ### Urgent Actions ({UrgentConfirmationCalls.Count})
        {FormatActionList(UrgentConfirmationCalls, "No urgent actions needed")}
        
        ### High Priority Calls ({HighPriorityConfirmationCalls.Count})
        {FormatActionList(HighPriorityConfirmationCalls, "No high priority calls needed")}
        
        ### Reminders to Send ({ReminderActions.Count})
        {FormatActionList(ReminderActions, "No additional reminders needed")}
        
        ### Overbooking Considerations ({OverbookConsiderations.Count})
        {FormatActionList(OverbookConsiderations, "No overbooking recommended")}
        """;

    private static string FormatActionList(List<ActionItem> items, string emptyMessage)
    {
        if (items.Count == 0) return emptyMessage;
        return string.Join("\n", items.Select(i => $"- **{i.PatientName}** ({i.AppointmentTime} with {i.ProviderName}) - {(i.IsHighRisk ? "High" : "Medium")} risk"));
    }
}

/// <summary>
/// Individual action item in a recommendation.
/// </summary>
public record ActionItem
{
    public string PatientName { get; init; } = string.Empty;
    public string AppointmentTime { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public string Rationale { get; init; } = string.Empty;
    [System.Text.Json.Serialization.JsonIgnore]
    internal bool IsHighRisk { get; init; }
}
