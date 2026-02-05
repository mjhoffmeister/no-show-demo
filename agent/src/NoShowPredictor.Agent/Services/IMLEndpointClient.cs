using NoShowPredictor.Agent.Models;

namespace NoShowPredictor.Agent.Services;

/// <summary>
/// Interface for ML endpoint client per ml-inference.openapi.yaml
/// </summary>
public interface IMLEndpointClient
{
    /// <summary>
    /// Get no-show probability predictions for one or more appointments.
    /// </summary>
    /// <param name="appointments">Appointments with patient, provider, department, and insurance data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Predictions with probabilities and risk factors</returns>
    Task<IReadOnlyList<Prediction>> GetPredictionsAsync(
        IEnumerable<Appointment> appointments,
        CancellationToken cancellationToken = default);
}
