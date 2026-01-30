using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using NoShowPredictor.Agent.Models;

namespace NoShowPredictor.Agent.Data;

/// <summary>
/// Repository interface for appointment data access.
/// </summary>
public interface IAppointmentRepository
{
    /// <summary>Get appointments for a date range with optional filters.</summary>
    Task<IReadOnlyList<Appointment>> GetAppointmentsAsync(
        DateOnly startDate,
        DateOnly endDate,
        int? providerId = null,
        int? departmentId = null,
        string? status = null,
        CancellationToken cancellationToken = default);

    /// <summary>Get a single appointment by ID with related entities.</summary>
    Task<Appointment?> GetAppointmentByIdAsync(int appointmentId, CancellationToken cancellationToken = default);

    /// <summary>Get high-risk appointments (probability >= threshold).</summary>
    Task<IReadOnlyList<Appointment>> GetHighRiskAppointmentsAsync(
        DateOnly startDate,
        DateOnly endDate,
        double probabilityThreshold = 0.4,
        CancellationToken cancellationToken = default);

    /// <summary>Get patient appointment history.</summary>
    Task<IReadOnlyList<Appointment>> GetPatientHistoryAsync(
        int patientId,
        int limit = 20,
        CancellationToken cancellationToken = default);

    /// <summary>Get prediction for an appointment.</summary>
    Task<Prediction?> GetPredictionAsync(int appointmentId, CancellationToken cancellationToken = default);

    /// <summary>Save a prediction.</summary>
    Task<Guid> SavePredictionAsync(Prediction prediction, CancellationToken cancellationToken = default);
}

/// <summary>
/// SQL Server implementation of appointment repository using Managed Identity authentication.
/// </summary>
public sealed class AppointmentRepository : IAppointmentRepository
{
    private readonly string _connectionString;
    private readonly ILogger<AppointmentRepository> _logger;

    public AppointmentRepository(string connectionString, ILogger<AppointmentRepository> logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<Appointment>> GetAppointmentsAsync(
        DateOnly startDate,
        DateOnly endDate,
        int? providerId = null,
        int? departmentId = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        const string baseSql = """
            SELECT a.*, p.*, pr.*, d.*
            FROM dbo.Appointments a
            INNER JOIN dbo.Patients p ON a.PatientId = p.PatientId
            INNER JOIN dbo.Providers pr ON a.ProviderId = pr.ProviderId
            INNER JOIN dbo.Departments d ON a.DepartmentId = d.DepartmentId
            WHERE a.AppointmentDate BETWEEN @StartDate AND @EndDate
            """;

        var filters = new List<string>();
        if (providerId.HasValue) filters.Add("AND a.ProviderId = @ProviderId");
        if (departmentId.HasValue) filters.Add("AND a.DepartmentId = @DepartmentId");
        if (!string.IsNullOrEmpty(status)) filters.Add("AND a.AppointmentStatus = @Status");

        var sql = baseSql + " " + string.Join(" ", filters) + " ORDER BY a.AppointmentDate, a.AppointmentStartTime";

        await using var connection = await CreateConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@StartDate", startDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@EndDate", endDate.ToDateTime(TimeOnly.MaxValue));
        if (providerId.HasValue) command.Parameters.AddWithValue("@ProviderId", providerId.Value);
        if (departmentId.HasValue) command.Parameters.AddWithValue("@DepartmentId", departmentId.Value);
        if (!string.IsNullOrEmpty(status)) command.Parameters.AddWithValue("@Status", status);

        return await ReadAppointmentsWithRelatedAsync(command, cancellationToken);
    }

    public async Task<Appointment?> GetAppointmentByIdAsync(int appointmentId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT a.*, p.*, pr.*, d.*
            FROM dbo.Appointments a
            INNER JOIN dbo.Patients p ON a.PatientId = p.PatientId
            INNER JOIN dbo.Providers pr ON a.ProviderId = pr.ProviderId
            INNER JOIN dbo.Departments d ON a.DepartmentId = d.DepartmentId
            WHERE a.AppointmentId = @AppointmentId
            """;

        await using var connection = await CreateConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@AppointmentId", appointmentId);

        var appointments = await ReadAppointmentsWithRelatedAsync(command, cancellationToken);
        return appointments.FirstOrDefault();
    }

    public async Task<IReadOnlyList<Appointment>> GetHighRiskAppointmentsAsync(
        DateOnly startDate,
        DateOnly endDate,
        double probabilityThreshold = 0.4,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT a.*, p.*, pr.*, d.*, pred.NoShowProbability
            FROM dbo.Appointments a
            INNER JOIN dbo.Patients p ON a.PatientId = p.PatientId
            INNER JOIN dbo.Providers pr ON a.ProviderId = pr.ProviderId
            INNER JOIN dbo.Departments d ON a.DepartmentId = d.DepartmentId
            LEFT JOIN dbo.Predictions pred ON a.AppointmentId = pred.AppointmentId
            WHERE a.AppointmentDate BETWEEN @StartDate AND @EndDate
              AND a.AppointmentStatus = 'Scheduled'
              AND (pred.NoShowProbability >= @Threshold OR pred.NoShowProbability IS NULL)
            ORDER BY pred.NoShowProbability DESC, a.AppointmentDate, a.AppointmentStartTime
            """;

        await using var connection = await CreateConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@StartDate", startDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@EndDate", endDate.ToDateTime(TimeOnly.MaxValue));
        command.Parameters.AddWithValue("@Threshold", probabilityThreshold);

        return await ReadAppointmentsWithRelatedAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<Appointment>> GetPatientHistoryAsync(
        int patientId,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var sql = $"""
            SELECT TOP ({limit}) a.*, p.*, pr.*, d.*
            FROM dbo.Appointments a
            INNER JOIN dbo.Patients p ON a.PatientId = p.PatientId
            INNER JOIN dbo.Providers pr ON a.ProviderId = pr.ProviderId
            INNER JOIN dbo.Departments d ON a.DepartmentId = d.DepartmentId
            WHERE a.PatientId = @PatientId
            ORDER BY a.AppointmentDate DESC
            """;

        await using var connection = await CreateConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@PatientId", patientId);

        return await ReadAppointmentsWithRelatedAsync(command, cancellationToken);
    }

    public async Task<Prediction?> GetPredictionAsync(int appointmentId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT prediction_id, appointmentid, no_show_probability, risk_level, risk_factors, model_version, predicted_at
            FROM dbo.Predictions
            WHERE appointmentid = @AppointmentId
            ORDER BY predicted_at DESC
            """;

        await using var connection = await CreateConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@AppointmentId", appointmentId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            var riskFactorsJson = reader.GetString(reader.GetOrdinal("risk_factors"));
            var riskFactors = string.IsNullOrEmpty(riskFactorsJson)
                ? []
                : JsonSerializer.Deserialize<List<RiskFactor>>(riskFactorsJson) ?? [];

            return new Prediction
            {
                PredictionId = reader.GetGuid(reader.GetOrdinal("prediction_id")),
                AppointmentId = reader.GetInt32(reader.GetOrdinal("appointmentid")),
                NoShowProbability = (double)reader.GetDecimal(reader.GetOrdinal("no_show_probability")),
                RiskLevel = reader.GetString(reader.GetOrdinal("risk_level")),
                RiskFactors = riskFactors,
                ModelVersion = reader.GetString(reader.GetOrdinal("model_version")),
                PredictedAt = reader.GetDateTime(reader.GetOrdinal("predicted_at"))
            };
        }

        return null;
    }

    public async Task<Guid> SavePredictionAsync(Prediction prediction, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO dbo.Predictions (appointmentid, no_show_probability, risk_level, risk_factors, model_version)
            OUTPUT INSERTED.prediction_id
            VALUES (@AppointmentId, @NoShowProbability, @RiskLevel, @RiskFactors, @ModelVersion)
            """;

        await using var connection = await CreateConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@AppointmentId", prediction.AppointmentId);
        command.Parameters.AddWithValue("@NoShowProbability", prediction.NoShowProbability);
        command.Parameters.AddWithValue("@RiskLevel", prediction.RiskLevel);
        command.Parameters.AddWithValue("@RiskFactors", JsonSerializer.Serialize(prediction.RiskFactors));
        command.Parameters.AddWithValue("@ModelVersion", prediction.ModelVersion);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return (Guid)result!;
    }

    private async Task<SqlConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        _logger.LogDebug("Opened SQL connection");
        return connection;
    }

    private static async Task<IReadOnlyList<Appointment>> ReadAppointmentsWithRelatedAsync(
        SqlCommand command,
        CancellationToken cancellationToken)
    {
        var appointments = new List<Appointment>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var patient = new Patient
            {
                PatientId = reader.GetInt32(reader.GetOrdinal("patientid")),
                EnterpriseId = reader.GetInt32(reader.GetOrdinal("enterpriseid")),
                PatientGender = reader.GetString(reader.GetOrdinal("patient_gender")),
                PatientAgeBucket = reader.GetString(reader.GetOrdinal("patient_age_bucket")),
                PatientRaceEthnicity = reader.IsDBNull(reader.GetOrdinal("patient_race_ethnicity")) ? null : reader.GetString(reader.GetOrdinal("patient_race_ethnicity")),
                PatientEmail = reader.IsDBNull(reader.GetOrdinal("patient_email")) ? null : reader.GetString(reader.GetOrdinal("patient_email")),
                PatientZipCode = reader.IsDBNull(reader.GetOrdinal("patient_zip_code")) ? null : reader.GetString(reader.GetOrdinal("patient_zip_code")),
                PortalEnterpriseId = reader.IsDBNull(reader.GetOrdinal("portal_enterpriseid")) ? null : reader.GetInt64(reader.GetOrdinal("portal_enterpriseid")),
                PortalLastLogin = reader.IsDBNull(reader.GetOrdinal("portal_last_login")) ? null : reader.GetDateTime(reader.GetOrdinal("portal_last_login"))
            };

            var provider = new Provider
            {
                ProviderId = reader.GetInt32(reader.GetOrdinal("providerid")),
                ProProviderId = reader.IsDBNull(reader.GetOrdinal("pro_providerid")) ? null : reader.GetInt32(reader.GetOrdinal("pro_providerid")),
                ProviderFirstName = reader.GetString(reader.GetOrdinal("providerfirstname")),
                ProviderLastName = reader.GetString(reader.GetOrdinal("providerlastname")),
                ProviderType = reader.GetString(reader.GetOrdinal("providertype")),
                ProviderTypeCategory = reader.IsDBNull(reader.GetOrdinal("providertypecategory")) ? null : reader.GetString(reader.GetOrdinal("providertypecategory")),
                ProviderSpecialty = reader.GetString(reader.GetOrdinal("provider_specialty")),
                ProviderSpecialtyServiceLine = reader.IsDBNull(reader.GetOrdinal("provider_specialty_service_line")) ? null : reader.GetString(reader.GetOrdinal("provider_specialty_service_line")),
                ProviderNpiNumber = reader.IsDBNull(reader.GetOrdinal("providernpinumber")) ? null : reader.GetString(reader.GetOrdinal("providernpinumber")),
                ProviderAffiliation = reader.IsDBNull(reader.GetOrdinal("provider_affiliation")) ? null : reader.GetString(reader.GetOrdinal("provider_affiliation")),
                EntityType = reader.IsDBNull(reader.GetOrdinal("entitytype")) ? null : reader.GetString(reader.GetOrdinal("entitytype")),
                BillableYN = reader.IsDBNull(reader.GetOrdinal("billableyn")) ? null : reader.GetString(reader.GetOrdinal("billableyn")),
                PatientFacingName = reader.IsDBNull(reader.GetOrdinal("patientfacingname")) ? null : reader.GetString(reader.GetOrdinal("patientfacingname"))
            };

            var department = new Department
            {
                DepartmentId = reader.GetInt32(reader.GetOrdinal("departmentid")),
                DepartmentName = reader.GetString(reader.GetOrdinal("departmentname")),
                DepartmentSpecialty = reader.IsDBNull(reader.GetOrdinal("departmentspecialty")) ? null : reader.GetString(reader.GetOrdinal("departmentspecialty")),
                BillingName = reader.IsDBNull(reader.GetOrdinal("billingname")) ? null : reader.GetString(reader.GetOrdinal("billingname")),
                PlaceOfServiceCode = reader.IsDBNull(reader.GetOrdinal("placeofservicecode")) ? null : reader.GetString(reader.GetOrdinal("placeofservicecode")),
                PlaceOfServiceType = reader.IsDBNull(reader.GetOrdinal("placeofservicetype")) ? null : reader.GetString(reader.GetOrdinal("placeofservicetype")),
                ProviderGroupId = reader.IsDBNull(reader.GetOrdinal("providergroupid")) ? null : reader.GetInt32(reader.GetOrdinal("providergroupid")),
                DepartmentGroup = reader.IsDBNull(reader.GetOrdinal("departmentgroup")) ? null : reader.GetString(reader.GetOrdinal("departmentgroup")),
                ContextId = reader.IsDBNull(reader.GetOrdinal("contextid")) ? null : reader.GetInt32(reader.GetOrdinal("contextid")),
                ContextName = reader.IsDBNull(reader.GetOrdinal("contextname")) ? null : reader.GetString(reader.GetOrdinal("contextname")),
                Market = reader.IsDBNull(reader.GetOrdinal("market")) ? null : reader.GetString(reader.GetOrdinal("market")),
                Division = reader.IsDBNull(reader.GetOrdinal("division")) ? null : reader.GetString(reader.GetOrdinal("division")),
                BusinessUnit = reader.IsDBNull(reader.GetOrdinal("business_unit")) ? null : reader.GetString(reader.GetOrdinal("business_unit"))
            };

            var appointment = new Appointment
            {
                AppointmentId = reader.GetInt32(reader.GetOrdinal("appointmentid")),
                ParentAppointmentId = reader.IsDBNull(reader.GetOrdinal("parentappointmentid")) ? null : reader.GetInt32(reader.GetOrdinal("parentappointmentid")),
                PatientId = reader.GetInt32(reader.GetOrdinal("patientid")),
                ProviderId = reader.GetInt32(reader.GetOrdinal("providerid")),
                DepartmentId = reader.GetInt32(reader.GetOrdinal("departmentid")),
                ReferringProviderId = reader.IsDBNull(reader.GetOrdinal("referringproviderid")) ? null : reader.GetInt32(reader.GetOrdinal("referringproviderid")),
                AppointmentDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("appointmentdate"))),
                AppointmentStartTime = reader.GetString(reader.GetOrdinal("appointmentstarttime")),
                AppointmentDuration = reader.GetInt32(reader.GetOrdinal("appointmentduration")),
                AppointmentTypeId = reader.GetInt32(reader.GetOrdinal("appointmenttypeid")),
                AppointmentTypeName = reader.GetString(reader.GetOrdinal("appointmenttypename")),
                AppointmentStatus = reader.GetString(reader.GetOrdinal("appointmentstatus")),
                AppointmentCreatedDateTime = reader.GetDateTime(reader.GetOrdinal("appointmentcreateddatetime")),
                AppointmentScheduledDateTime = reader.GetDateTime(reader.GetOrdinal("appointmentscheduleddatetime")),
                AppointmentCheckInDateTime = reader.IsDBNull(reader.GetOrdinal("appointmentcheckindatetime")) ? null : reader.GetDateTime(reader.GetOrdinal("appointmentcheckindatetime")),
                AppointmentCheckOutDateTime = reader.IsDBNull(reader.GetOrdinal("appointmentcheckoutdatetime")) ? null : reader.GetDateTime(reader.GetOrdinal("appointmentcheckoutdatetime")),
                AppointmentCancelledDateTime = reader.IsDBNull(reader.GetOrdinal("appointmentcancelleddatetime")) ? null : reader.GetDateTime(reader.GetOrdinal("appointmentcancelleddatetime")),
                WebSchedulableYN = reader.IsDBNull(reader.GetOrdinal("webschedulableyn")) ? null : reader.GetInt32(reader.GetOrdinal("webschedulableyn")),
                VirtualFlag = reader.GetString(reader.GetOrdinal("virtual_flag")),
                NewPatientFlag = reader.GetString(reader.GetOrdinal("new_patient_flag")),
                Patient = patient,
                Provider = provider,
                Department = department
            };

            appointments.Add(appointment);
        }

        return appointments;
    }
}
