using NoShowPredictor.Agent.Models;

namespace NoShowPredictor.Agent.Data;

/// <summary>
/// Interface for accessing appointment data.
/// </summary>
public interface IAppointmentRepository
{
    /// <summary>
    /// Get appointments within a date range with patient, provider, department, and insurance data.
    /// </summary>
    Task<IReadOnlyList<Appointment>> GetAppointmentsByDateRangeAsync(
        DateOnly startDate,
        DateOnly endDate,
        string? riskLevelFilter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get appointments for a specific patient.
    /// </summary>
    Task<IReadOnlyList<Appointment>> GetAppointmentsByPatientAsync(
        int patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get appointments for a specific provider within a date range.
    /// </summary>
    Task<IReadOnlyList<Appointment>> GetAppointmentsByProviderAsync(
        int providerId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Search for patients by name.
    /// </summary>
    Task<IReadOnlyList<Patient>> SearchPatientsAsync(
        string nameQuery,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculate historical no-show statistics for a patient.
    /// </summary>
    Task<(int totalAppointments, int noShowCount, decimal noShowRate)> GetPatientNoShowStatsAsync(
        int patientId,
        CancellationToken cancellationToken = default);
}
