using System.Data;
using Azure.Core;
using Azure.Identity;
using Microsoft.Data.SqlClient;
using NoShowPredictor.Agent.Models;

namespace NoShowPredictor.Agent.Data;

/// <summary>
/// Repository for accessing appointment data from Azure SQL Database.
/// Uses Managed Identity (DefaultAzureCredential) for authentication.
/// </summary>
public class AppointmentRepository : IAppointmentRepository
{
    private readonly string _connectionString;
    private readonly TokenCredential _credential;

    public AppointmentRepository(string connectionString, TokenCredential? credential = null)
    {
        _connectionString = connectionString;
        _credential = credential ?? new DefaultAzureCredential();
    }

    /// <summary>
    /// Get appointments within a date range with patient, provider, department, and insurance data.
    /// </summary>
    public async Task<IReadOnlyList<Appointment>> GetAppointmentsByDateRangeAsync(
        DateOnly startDate,
        DateOnly endDate,
        string? riskLevelFilter = null,
        CancellationToken cancellationToken = default)
    {
        var appointments = new List<Appointment>();

        await using var connection = await CreateConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                a.appointmentid, a.parentappointmentid, a.patientid, a.providerid, a.departmentid,
                a.referringproviderid, a.referralauthid, a.appointmentdate, a.appointmentstarttime,
                a.appointmentdatetime, a.appointmentduration, a.appointmenttypeid, a.appointmenttypename,
                a.appointmentstatus, a.appointmentcreateddatetime, a.appointmentcreatedby,
                a.appointmentscheduleddatetime, a.scheduledby, a.appointmentcheckindatetime,
                a.appointmentcheckoutdatetime, a.appointmentcancelleddatetime, a.cancelledby,
                a.appointmentcancelreason, a.rescheduledappointmentid, a.rescheduleddatetime,
                a.rescheduledby, a.startcheckindatetime, a.stopsignoffdatetime,
                a.appointmentdeleteddatetime, a.claimid, a.cycletime, a.frozenyn,
                a.appointmentfrozenreason, a.virtual_flag, a.new_patient_flag, a.webschedulableyn,
                p.patientid AS pat_id, p.enterpriseid, p.patient_gender, p.patient_age_bucket,
                p.patient_race_ethnicity, p.patient_email, p.patient_zip_code,
                p.portal_enterpriseid, p.portal_last_login,
                p.historical_no_show_rate, p.historical_no_show_count,
                pr.providerid AS prov_id, pr.pro_providerid, pr.providerfirstname, pr.providerlastname,
                pr.providertype, pr.providertypecategory, pr.provider_specialty,
                pr.provider_specialty_service_line, pr.providernpinumber, pr.provider_affiliation,
                pr.entitytype, pr.billableyn, pr.patientfacingname,
                d.departmentid AS dept_id, d.departmentname, d.departmentspecialty, d.billingname,
                d.placeofservicecode, d.placeofservicetype, d.providergroupid, d.departmentgroup,
                d.contextid, d.contextname, d.market, d.division, d.business_unit,
                i.primarypatientinsuranceid, i.sipg1, i.sipg2,
                i.insurance_plan_1_company_description, i.insurance_group_id
            FROM appointments a
            LEFT JOIN patients p ON a.patientid = p.patientid
            LEFT JOIN providers pr ON a.providerid = pr.providerid
            LEFT JOIN departments d ON a.departmentid = d.departmentid
            LEFT JOIN insurance i ON a.patientid = i.patientid
            WHERE a.appointmentdate >= @startDate 
              AND a.appointmentdate <= @endDate
              AND a.appointmentstatus = 'Scheduled'
            ORDER BY a.appointmentdatetime
            """;

        command.Parameters.AddWithValue("@startDate", startDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@endDate", endDate.ToDateTime(TimeOnly.MaxValue));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            appointments.Add(MapAppointment(reader));
        }

        return appointments;
    }

    /// <summary>
    /// Get appointments for a specific patient.
    /// </summary>
    public async Task<IReadOnlyList<Appointment>> GetAppointmentsByPatientAsync(
        int patientId,
        CancellationToken cancellationToken = default)
    {
        var appointments = new List<Appointment>();

        await using var connection = await CreateConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT TOP 50
                a.appointmentid, a.parentappointmentid, a.patientid, a.providerid, a.departmentid,
                a.referringproviderid, a.referralauthid, a.appointmentdate, a.appointmentstarttime,
                a.appointmentdatetime, a.appointmentduration, a.appointmenttypeid, a.appointmenttypename,
                a.appointmentstatus, a.appointmentcreateddatetime, a.appointmentcreatedby,
                a.appointmentscheduleddatetime, a.scheduledby, a.appointmentcheckindatetime,
                a.appointmentcheckoutdatetime, a.appointmentcancelleddatetime, a.cancelledby,
                a.appointmentcancelreason, a.rescheduledappointmentid, a.rescheduleddatetime,
                a.rescheduledby, a.startcheckindatetime, a.stopsignoffdatetime,
                a.appointmentdeleteddatetime, a.claimid, a.cycletime, a.frozenyn,
                a.appointmentfrozenreason, a.virtual_flag, a.new_patient_flag, a.webschedulableyn,
                p.patientid AS pat_id, p.enterpriseid, p.patient_gender, p.patient_age_bucket,
                p.patient_race_ethnicity, p.patient_email, p.patient_zip_code,
                p.portal_enterpriseid, p.portal_last_login,
                p.historical_no_show_rate, p.historical_no_show_count,
                pr.providerid AS prov_id, pr.pro_providerid, pr.providerfirstname, pr.providerlastname,
                pr.providertype, pr.providertypecategory, pr.provider_specialty,
                pr.provider_specialty_service_line, pr.providernpinumber, pr.provider_affiliation,
                pr.entitytype, pr.billableyn, pr.patientfacingname,
                d.departmentid AS dept_id, d.departmentname, d.departmentspecialty, d.billingname,
                d.placeofservicecode, d.placeofservicetype, d.providergroupid, d.departmentgroup,
                d.contextid, d.contextname, d.market, d.division, d.business_unit,
                i.primarypatientinsuranceid, i.sipg1, i.sipg2,
                i.insurance_plan_1_company_description, i.insurance_group_id
            FROM appointments a
            LEFT JOIN patients p ON a.patientid = p.patientid
            LEFT JOIN providers pr ON a.providerid = pr.providerid
            LEFT JOIN departments d ON a.departmentid = d.departmentid
            LEFT JOIN insurance i ON a.patientid = i.patientid
            WHERE a.patientid = @patientId
            ORDER BY a.appointmentdatetime DESC
            """;

        command.Parameters.AddWithValue("@patientId", patientId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            appointments.Add(MapAppointment(reader));
        }

        return appointments;
    }

    /// <summary>
    /// Get appointments for a specific provider within a date range.
    /// </summary>
    public async Task<IReadOnlyList<Appointment>> GetAppointmentsByProviderAsync(
        int providerId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var appointments = new List<Appointment>();

        await using var connection = await CreateConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                a.appointmentid, a.parentappointmentid, a.patientid, a.providerid, a.departmentid,
                a.referringproviderid, a.referralauthid, a.appointmentdate, a.appointmentstarttime,
                a.appointmentdatetime, a.appointmentduration, a.appointmenttypeid, a.appointmenttypename,
                a.appointmentstatus, a.appointmentcreateddatetime, a.appointmentcreatedby,
                a.appointmentscheduleddatetime, a.scheduledby, a.appointmentcheckindatetime,
                a.appointmentcheckoutdatetime, a.appointmentcancelleddatetime, a.cancelledby,
                a.appointmentcancelreason, a.rescheduledappointmentid, a.rescheduleddatetime,
                a.rescheduledby, a.startcheckindatetime, a.stopsignoffdatetime,
                a.appointmentdeleteddatetime, a.claimid, a.cycletime, a.frozenyn,
                a.appointmentfrozenreason, a.virtual_flag, a.new_patient_flag, a.webschedulableyn,
                p.patientid AS pat_id, p.enterpriseid, p.patient_gender, p.patient_age_bucket,
                p.patient_race_ethnicity, p.patient_email, p.patient_zip_code,
                p.portal_enterpriseid, p.portal_last_login,
                p.historical_no_show_rate, p.historical_no_show_count,
                pr.providerid AS prov_id, pr.pro_providerid, pr.providerfirstname, pr.providerlastname,
                pr.providertype, pr.providertypecategory, pr.provider_specialty,
                pr.provider_specialty_service_line, pr.providernpinumber, pr.provider_affiliation,
                pr.entitytype, pr.billableyn, pr.patientfacingname,
                d.departmentid AS dept_id, d.departmentname, d.departmentspecialty, d.billingname,
                d.placeofservicecode, d.placeofservicetype, d.providergroupid, d.departmentgroup,
                d.contextid, d.contextname, d.market, d.division, d.business_unit,
                i.primarypatientinsuranceid, i.sipg1, i.sipg2,
                i.insurance_plan_1_company_description, i.insurance_group_id
            FROM appointments a
            LEFT JOIN patients p ON a.patientid = p.patientid
            LEFT JOIN providers pr ON a.providerid = pr.providerid
            LEFT JOIN departments d ON a.departmentid = d.departmentid
            LEFT JOIN insurance i ON a.patientid = i.patientid
            WHERE a.providerid = @providerId
              AND a.appointmentdate >= @startDate 
              AND a.appointmentdate <= @endDate
              AND a.appointmentstatus = 'Scheduled'
            ORDER BY a.appointmentdatetime
            """;

        command.Parameters.AddWithValue("@providerId", providerId);
        command.Parameters.AddWithValue("@startDate", startDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@endDate", endDate.ToDateTime(TimeOnly.MaxValue));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            appointments.Add(MapAppointment(reader));
        }

        return appointments;
    }

    /// <summary>
    /// Search for patients by name.
    /// </summary>
    public async Task<IReadOnlyList<Patient>> SearchPatientsAsync(
        string nameQuery,
        CancellationToken cancellationToken = default)
    {
        var patients = new List<Patient>();

        await using var connection = await CreateConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        // Note: In production, would search on a patient name field
        // For synthetic data, we search by patient_email which contains names
        command.CommandText = """
            SELECT TOP 10
                patientid, enterpriseid, patient_gender, patient_age_bucket,
                patient_race_ethnicity, patient_email, patient_zip_code,
                portal_enterpriseid, portal_last_login,
                historical_no_show_rate, historical_no_show_count
            FROM patients
            WHERE patient_email LIKE @nameQuery
            ORDER BY patientid
            """;

        command.Parameters.AddWithValue("@nameQuery", $"%{nameQuery}%");

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            patients.Add(new Patient
            {
                PatientId = reader.GetInt32(reader.GetOrdinal("patientid")),
                EnterpriseId = reader.IsDBNull(reader.GetOrdinal("enterpriseid")) ? null : reader.GetInt32(reader.GetOrdinal("enterpriseid")),
                PatientGender = reader.GetString(reader.GetOrdinal("patient_gender")),
                PatientAgeBucket = reader.GetString(reader.GetOrdinal("patient_age_bucket")),
                PatientRaceEthnicity = reader.IsDBNull(reader.GetOrdinal("patient_race_ethnicity")) ? null : reader.GetString(reader.GetOrdinal("patient_race_ethnicity")),
                PatientEmail = reader.IsDBNull(reader.GetOrdinal("patient_email")) ? null : reader.GetString(reader.GetOrdinal("patient_email")),
                PatientZipCode = reader.IsDBNull(reader.GetOrdinal("patient_zip_code")) ? null : reader.GetString(reader.GetOrdinal("patient_zip_code")),
                PortalEnterpriseId = reader.IsDBNull(reader.GetOrdinal("portal_enterpriseid")) ? null : reader.GetInt64(reader.GetOrdinal("portal_enterpriseid")),
                PortalLastLogin = reader.IsDBNull(reader.GetOrdinal("portal_last_login")) ? null : reader.GetDateTime(reader.GetOrdinal("portal_last_login")),
                HistoricalNoShowRate = reader.IsDBNull(reader.GetOrdinal("historical_no_show_rate")) ? null : reader.GetDecimal(reader.GetOrdinal("historical_no_show_rate")),
                HistoricalNoShowCount = reader.IsDBNull(reader.GetOrdinal("historical_no_show_count")) ? null : reader.GetInt32(reader.GetOrdinal("historical_no_show_count"))
            });
        }

        return patients;
    }

    /// <summary>
    /// Calculate historical no-show statistics for a patient.
    /// </summary>
    public async Task<(int totalAppointments, int noShowCount, decimal noShowRate)> GetPatientNoShowStatsAsync(
        int patientId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await CreateConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT 
                COUNT(*) AS total_appointments,
                SUM(CASE WHEN appointmentstatus = 'No Show' THEN 1 ELSE 0 END) AS no_show_count
            FROM appointments
            WHERE patientid = @patientId
              AND appointmentdatetime < GETUTCDATE()
            """;

        command.Parameters.AddWithValue("@patientId", patientId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (await reader.ReadAsync(cancellationToken))
        {
            var total = reader.GetInt32(0);
            var noShows = reader.GetInt32(1);
            var rate = total > 0 ? (decimal)noShows / total : 0m;
            return (total, noShows, rate);
        }

        return (0, 0, 0m);
    }

    private async Task<SqlConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_connectionString);

        // Only set access token if not using connection string authentication
        // (i.e., if "Authentication=" is not in the connection string)
        if (!_connectionString.Contains("Authentication=", StringComparison.OrdinalIgnoreCase))
        {
            // Get token for Azure SQL using Managed Identity
            var tokenRequest = new TokenRequestContext(new[] { "https://database.windows.net/.default" });
            var token = await _credential.GetTokenAsync(tokenRequest, cancellationToken);
            connection.AccessToken = token.Token;
        }

        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static Appointment MapAppointment(SqlDataReader reader)
    {
        return new Appointment
        {
            AppointmentId = reader.GetInt32(reader.GetOrdinal("appointmentid")),
            ParentAppointmentId = reader.IsDBNull(reader.GetOrdinal("parentappointmentid")) ? null : reader.GetInt32(reader.GetOrdinal("parentappointmentid")),
            PatientId = reader.GetInt32(reader.GetOrdinal("patientid")),
            ProviderId = reader.GetInt32(reader.GetOrdinal("providerid")),
            DepartmentId = reader.GetInt32(reader.GetOrdinal("departmentid")),
            ReferringProviderId = reader.IsDBNull(reader.GetOrdinal("referringproviderid")) ? null : reader.GetInt32(reader.GetOrdinal("referringproviderid")),
            ReferralAuthId = reader.IsDBNull(reader.GetOrdinal("referralauthid")) ? null : reader.GetInt32(reader.GetOrdinal("referralauthid")),
            AppointmentDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("appointmentdate"))),
            AppointmentStartTime = reader.GetString(reader.GetOrdinal("appointmentstarttime")),
            AppointmentDateTime = reader.GetDateTime(reader.GetOrdinal("appointmentdatetime")),
            AppointmentDuration = reader.GetInt32(reader.GetOrdinal("appointmentduration")),
            AppointmentTypeId = reader.GetInt32(reader.GetOrdinal("appointmenttypeid")),
            AppointmentTypeName = reader.GetString(reader.GetOrdinal("appointmenttypename")),
            AppointmentStatus = reader.GetString(reader.GetOrdinal("appointmentstatus")),
            AppointmentCreatedDateTime = reader.GetDateTime(reader.GetOrdinal("appointmentcreateddatetime")),
            AppointmentCreatedBy = reader.IsDBNull(reader.GetOrdinal("appointmentcreatedby")) ? null : reader.GetString(reader.GetOrdinal("appointmentcreatedby")),
            AppointmentScheduledDateTime = reader.GetDateTime(reader.GetOrdinal("appointmentscheduleddatetime")),
            ScheduledBy = reader.IsDBNull(reader.GetOrdinal("scheduledby")) ? null : reader.GetString(reader.GetOrdinal("scheduledby")),
            AppointmentCheckinDateTime = reader.IsDBNull(reader.GetOrdinal("appointmentcheckindatetime")) ? null : reader.GetDateTime(reader.GetOrdinal("appointmentcheckindatetime")),
            AppointmentCheckoutDateTime = reader.IsDBNull(reader.GetOrdinal("appointmentcheckoutdatetime")) ? null : reader.GetDateTime(reader.GetOrdinal("appointmentcheckoutdatetime")),
            AppointmentCancelledDateTime = reader.IsDBNull(reader.GetOrdinal("appointmentcancelleddatetime")) ? null : reader.GetDateTime(reader.GetOrdinal("appointmentcancelleddatetime")),
            CancelledBy = reader.IsDBNull(reader.GetOrdinal("cancelledby")) ? null : reader.GetString(reader.GetOrdinal("cancelledby")),
            AppointmentCancelReason = reader.IsDBNull(reader.GetOrdinal("appointmentcancelreason")) ? null : reader.GetString(reader.GetOrdinal("appointmentcancelreason")),
            RescheduledAppointmentId = reader.IsDBNull(reader.GetOrdinal("rescheduledappointmentid")) ? null : reader.GetInt32(reader.GetOrdinal("rescheduledappointmentid")),
            RescheduledDateTime = reader.IsDBNull(reader.GetOrdinal("rescheduleddatetime")) ? null : reader.GetDateTime(reader.GetOrdinal("rescheduleddatetime")),
            RescheduledBy = reader.IsDBNull(reader.GetOrdinal("rescheduledby")) ? null : reader.GetString(reader.GetOrdinal("rescheduledby")),
            StartCheckinDateTime = reader.IsDBNull(reader.GetOrdinal("startcheckindatetime")) ? null : reader.GetDateTime(reader.GetOrdinal("startcheckindatetime")),
            StopSignoffDateTime = reader.IsDBNull(reader.GetOrdinal("stopsignoffdatetime")) ? null : reader.GetDateTime(reader.GetOrdinal("stopsignoffdatetime")),
            AppointmentDeletedDateTime = reader.IsDBNull(reader.GetOrdinal("appointmentdeleteddatetime")) ? null : reader.GetDateTime(reader.GetOrdinal("appointmentdeleteddatetime")),
            ClaimId = reader.IsDBNull(reader.GetOrdinal("claimid")) ? null : reader.GetInt32(reader.GetOrdinal("claimid")),
            CycleTime = reader.IsDBNull(reader.GetOrdinal("cycletime")) ? null : reader.GetDecimal(reader.GetOrdinal("cycletime")),
            FrozenYn = reader.IsDBNull(reader.GetOrdinal("frozenyn")) ? null : reader.GetString(reader.GetOrdinal("frozenyn")),
            AppointmentFrozenReason = reader.IsDBNull(reader.GetOrdinal("appointmentfrozenreason")) ? null : reader.GetString(reader.GetOrdinal("appointmentfrozenreason")),
            VirtualFlag = reader.GetString(reader.GetOrdinal("virtual_flag")),
            NewPatientFlag = reader.GetString(reader.GetOrdinal("new_patient_flag")),
            WebSchedulableYn = reader.IsDBNull(reader.GetOrdinal("webschedulableyn")) ? null : reader.GetInt32(reader.GetOrdinal("webschedulableyn")),
            Patient = new Patient
            {
                PatientId = reader.GetInt32(reader.GetOrdinal("pat_id")),
                EnterpriseId = reader.IsDBNull(reader.GetOrdinal("enterpriseid")) ? null : reader.GetInt32(reader.GetOrdinal("enterpriseid")),
                PatientGender = reader.GetString(reader.GetOrdinal("patient_gender")),
                PatientAgeBucket = reader.GetString(reader.GetOrdinal("patient_age_bucket")),
                PatientRaceEthnicity = reader.IsDBNull(reader.GetOrdinal("patient_race_ethnicity")) ? null : reader.GetString(reader.GetOrdinal("patient_race_ethnicity")),
                PatientEmail = reader.IsDBNull(reader.GetOrdinal("patient_email")) ? null : reader.GetString(reader.GetOrdinal("patient_email")),
                PatientZipCode = reader.IsDBNull(reader.GetOrdinal("patient_zip_code")) ? null : reader.GetString(reader.GetOrdinal("patient_zip_code")),
                PortalEnterpriseId = reader.IsDBNull(reader.GetOrdinal("portal_enterpriseid")) ? null : reader.GetInt64(reader.GetOrdinal("portal_enterpriseid")),
                PortalLastLogin = reader.IsDBNull(reader.GetOrdinal("portal_last_login")) ? null : reader.GetDateTime(reader.GetOrdinal("portal_last_login")),
                HistoricalNoShowRate = reader.IsDBNull(reader.GetOrdinal("historical_no_show_rate")) ? null : reader.GetDecimal(reader.GetOrdinal("historical_no_show_rate")),
                HistoricalNoShowCount = reader.IsDBNull(reader.GetOrdinal("historical_no_show_count")) ? null : reader.GetInt32(reader.GetOrdinal("historical_no_show_count"))
            },
            Provider = new Provider
            {
                ProviderId = reader.GetInt32(reader.GetOrdinal("prov_id")),
                ProProviderIdSource = reader.IsDBNull(reader.GetOrdinal("pro_providerid")) ? null : reader.GetInt32(reader.GetOrdinal("pro_providerid")),
                ProviderFirstName = reader.GetString(reader.GetOrdinal("providerfirstname")),
                ProviderLastName = reader.GetString(reader.GetOrdinal("providerlastname")),
                ProviderType = reader.GetString(reader.GetOrdinal("providertype")),
                ProviderTypeCategory = reader.IsDBNull(reader.GetOrdinal("providertypecategory")) ? null : reader.GetString(reader.GetOrdinal("providertypecategory")),
                ProviderSpecialty = reader.GetString(reader.GetOrdinal("provider_specialty")),
                ProviderSpecialtyServiceLine = reader.IsDBNull(reader.GetOrdinal("provider_specialty_service_line")) ? null : reader.GetString(reader.GetOrdinal("provider_specialty_service_line")),
                ProviderNpiNumber = reader.IsDBNull(reader.GetOrdinal("providernpinumber")) ? null : reader.GetString(reader.GetOrdinal("providernpinumber")),
                ProviderAffiliation = reader.IsDBNull(reader.GetOrdinal("provider_affiliation")) ? null : reader.GetString(reader.GetOrdinal("provider_affiliation")),
                EntityType = reader.IsDBNull(reader.GetOrdinal("entitytype")) ? null : reader.GetString(reader.GetOrdinal("entitytype")),
                BillableYn = reader.IsDBNull(reader.GetOrdinal("billableyn")) ? null : reader.GetString(reader.GetOrdinal("billableyn")),
                PatientFacingName = reader.IsDBNull(reader.GetOrdinal("patientfacingname")) ? null : reader.GetString(reader.GetOrdinal("patientfacingname"))
            },
            Department = new Department
            {
                DepartmentId = reader.GetInt32(reader.GetOrdinal("dept_id")),
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
            },
            Insurance = reader.IsDBNull(reader.GetOrdinal("primarypatientinsuranceid")) ? null : new Insurance
            {
                PrimaryPatientInsuranceId = reader.GetInt32(reader.GetOrdinal("primarypatientinsuranceid")),
                PatientId = reader.GetInt32(reader.GetOrdinal("patientid")),
                Sipg1 = reader.IsDBNull(reader.GetOrdinal("sipg1")) ? null : reader.GetString(reader.GetOrdinal("sipg1")),
                Sipg2 = reader.IsDBNull(reader.GetOrdinal("sipg2")) ? null : reader.GetString(reader.GetOrdinal("sipg2")),
                InsurancePlan1CompanyDescription = reader.IsDBNull(reader.GetOrdinal("insurance_plan_1_company_description")) ? null : reader.GetString(reader.GetOrdinal("insurance_plan_1_company_description")),
                InsuranceGroupId = reader.IsDBNull(reader.GetOrdinal("insurance_group_id")) ? null : reader.GetString(reader.GetOrdinal("insurance_group_id"))
            }
        };
    }
}
