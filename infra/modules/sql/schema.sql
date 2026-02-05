-- =============================================================================
-- Schema: Medical Appointment No-Show Predictor
-- =============================================================================
-- Tables aligned with data-model.md entity definitions
-- Run this script after database creation via seed_database.py
-- =============================================================================

-- -----------------------------------------------------------------------------
-- Table: Patients
-- -----------------------------------------------------------------------------
IF OBJECT_ID('dbo.vw_AppointmentsWithMetrics', 'V') IS NOT NULL DROP VIEW dbo.vw_AppointmentsWithMetrics;
IF OBJECT_ID('dbo.Predictions', 'U') IS NOT NULL DROP TABLE dbo.Predictions;
IF OBJECT_ID('dbo.Appointments', 'U') IS NOT NULL DROP TABLE dbo.Appointments;
IF OBJECT_ID('dbo.Insurance', 'U') IS NOT NULL DROP TABLE dbo.Insurance;
IF OBJECT_ID('dbo.Patients', 'U') IS NOT NULL DROP TABLE dbo.Patients;
IF OBJECT_ID('dbo.Providers', 'U') IS NOT NULL DROP TABLE dbo.Providers;
IF OBJECT_ID('dbo.Departments', 'U') IS NOT NULL DROP TABLE dbo.Departments;
GO

CREATE TABLE dbo.Patients (
    patientid               INT             NOT NULL PRIMARY KEY,
    enterpriseid            INT             NOT NULL UNIQUE,
    patient_gender          NVARCHAR(10)    NOT NULL CHECK (patient_gender IN ('M', 'F', 'Other')),
    patient_age_bucket      NVARCHAR(10)    NOT NULL CHECK (patient_age_bucket IN ('0-17', '18-39', '40-64', '65+')),
    patient_race_ethnicity  NVARCHAR(100)   NULL,
    patient_email           NVARCHAR(255)   NULL,
    patient_zip_code        NVARCHAR(5)     NULL CHECK (LEN(patient_zip_code) = 5 OR patient_zip_code IS NULL),
    portal_enterpriseid     BIGINT          NULL,
    portal_last_login       DATETIME2       NULL,
    historical_no_show_count INT            NULL DEFAULT 0,
    historical_no_show_rate  DECIMAL(5,4)   NULL DEFAULT 0.0
);

CREATE INDEX IX_Patients_EnterpriseId ON dbo.Patients(enterpriseid);
GO

-- -----------------------------------------------------------------------------
-- Table: Providers
-- -----------------------------------------------------------------------------
CREATE TABLE dbo.Providers (
    providerid                      INT             NOT NULL PRIMARY KEY,
    pro_providerid                  INT             NULL,
    providerfirstname               NVARCHAR(100)   NOT NULL,
    providerlastname                NVARCHAR(100)   NOT NULL,
    providertype                    NVARCHAR(50)    NOT NULL,
    providertypecategory            NVARCHAR(50)    NULL,
    provider_specialty              NVARCHAR(100)   NOT NULL,
    provider_specialty_service_line NVARCHAR(100)   NULL,
    providernpinumber               NVARCHAR(20)    NULL,
    provider_affiliation            NVARCHAR(50)    NULL,
    entitytype                      NVARCHAR(50)    NULL,
    billableyn                      NVARCHAR(1)     NULL CHECK (billableyn IN ('Y', 'N') OR billableyn IS NULL),
    patientfacingname               NVARCHAR(200)   NULL
);
GO

-- -----------------------------------------------------------------------------
-- Table: Departments
-- -----------------------------------------------------------------------------
CREATE TABLE dbo.Departments (
    departmentid            INT             NOT NULL PRIMARY KEY,
    departmentname          NVARCHAR(200)   NOT NULL,
    departmentspecialty     NVARCHAR(100)   NULL,
    billingname             NVARCHAR(200)   NULL,
    placeofservicecode      NVARCHAR(10)    NULL,
    placeofservicetype      NVARCHAR(50)    NULL,
    providergroupid         INT             NULL,
    departmentgroup         NVARCHAR(100)   NULL,
    contextid               INT             NULL,
    contextname             NVARCHAR(50)    NULL,
    market                  NVARCHAR(100)   NULL,
    division                NVARCHAR(100)   NULL,
    business_unit           NVARCHAR(50)    NULL
);
GO

-- -----------------------------------------------------------------------------
-- Table: Insurance
-- -----------------------------------------------------------------------------
CREATE TABLE dbo.Insurance (
    primarypatientinsuranceid               INT             NOT NULL PRIMARY KEY,
    patientid                               INT             NOT NULL,
    sipg1                                   NVARCHAR(100)   NULL,
    sipg2                                   NVARCHAR(100)   NULL,
    insurance_plan_1_company_description    NVARCHAR(200)   NULL,
    insurance_group_id                      NVARCHAR(50)    NULL,
    
    CONSTRAINT FK_Insurance_Patients FOREIGN KEY (patientid) REFERENCES dbo.Patients(patientid)
);
GO

-- -----------------------------------------------------------------------------
-- Table: Appointments
-- -----------------------------------------------------------------------------
CREATE TABLE dbo.Appointments (
    appointmentid                   INT             NOT NULL PRIMARY KEY,
    parentappointmentid             INT             NULL,
    patientid                       INT             NOT NULL,
    providerid                      INT             NOT NULL,
    departmentid                    INT             NOT NULL,
    referringproviderid             INT             NULL,
    referralauthid                  INT             NULL,
    appointmentdate                 DATE            NOT NULL,
    appointmentstarttime            NVARCHAR(5)     NOT NULL,
    appointmentdatetime             DATETIME2       NOT NULL,
    appointmentduration             INT             NOT NULL,
    appointmenttypeid               INT             NOT NULL,
    appointmenttypename             NVARCHAR(200)   NOT NULL,
    appointmentstatus               NVARCHAR(50)    NOT NULL,
    appointmentcreateddatetime      DATETIME2       NOT NULL,
    appointmentcreatedby            NVARCHAR(100)   NULL,
    appointmentscheduleddatetime    DATETIME2       NOT NULL,
    scheduledby                     NVARCHAR(100)   NULL,
    appointmentcheckindatetime      DATETIME2       NULL,
    appointmentcheckoutdatetime     DATETIME2       NULL,
    appointmentcancelleddatetime    DATETIME2       NULL,
    cancelledby                     NVARCHAR(100)   NULL,
    appointmentcancelreason         NVARCHAR(200)   NULL,
    rescheduledappointmentid        INT             NULL,
    rescheduleddatetime             DATETIME2       NULL,
    rescheduledby                   NVARCHAR(100)   NULL,
    startcheckindatetime            DATETIME2       NULL,
    stopsignoffdatetime             DATETIME2       NULL,
    appointmentdeleteddatetime      DATETIME2       NULL,
    claimid                         INT             NULL,
    cycletime                       DECIMAL(10,2)   NULL,
    frozenyn                        NVARCHAR(1)     NULL CHECK (frozenyn IN ('Y', 'N') OR frozenyn IS NULL),
    appointmentfrozenreason         NVARCHAR(200)   NULL,
    virtual_flag                    NVARCHAR(20)    NOT NULL CHECK (virtual_flag IN ('Virtual-Telephone', 'Virtual-Video', 'Non-Virtual')),
    new_patient_flag                NVARCHAR(20)    NOT NULL CHECK (new_patient_flag IN ('NEW PATIENT', 'EST PATIENT')),
    webschedulableyn                INT             NULL CHECK (webschedulableyn IN (0, 1) OR webschedulableyn IS NULL),
    primarypatientinsuranceid       INT             NULL,
    
    CONSTRAINT FK_Appointments_Patients FOREIGN KEY (patientid) REFERENCES dbo.Patients(patientid),
    CONSTRAINT FK_Appointments_Providers FOREIGN KEY (providerid) REFERENCES dbo.Providers(providerid),
    CONSTRAINT FK_Appointments_Departments FOREIGN KEY (departmentid) REFERENCES dbo.Departments(departmentid),
    CONSTRAINT FK_Appointments_Insurance FOREIGN KEY (primarypatientinsuranceid) REFERENCES dbo.Insurance(primarypatientinsuranceid)
);

-- Performance indexes
CREATE INDEX IX_Appointments_Date ON dbo.Appointments(appointmentdate);
CREATE INDEX IX_Appointments_DateTime ON dbo.Appointments(appointmentdatetime);
CREATE INDEX IX_Appointments_Patient ON dbo.Appointments(patientid);
CREATE INDEX IX_Appointments_Provider_Date ON dbo.Appointments(providerid, appointmentdate);
CREATE INDEX IX_Appointments_Department ON dbo.Appointments(departmentid, appointmentdate);
CREATE INDEX IX_Appointments_Status ON dbo.Appointments(appointmentstatus);
GO

-- -----------------------------------------------------------------------------
-- Table: Predictions (populated at runtime by ML endpoint)
-- -----------------------------------------------------------------------------
CREATE TABLE dbo.Predictions (
    prediction_id           UNIQUEIDENTIFIER    NOT NULL PRIMARY KEY DEFAULT NEWID(),
    appointmentid           INT                 NOT NULL,
    no_show_probability     DECIMAL(5,4)        NOT NULL CHECK (no_show_probability >= 0 AND no_show_probability <= 1),
    risk_level              NVARCHAR(10)        NOT NULL CHECK (risk_level IN ('Low', 'Medium', 'High')),
    risk_factors            NVARCHAR(MAX)       NOT NULL,  -- JSON array
    model_version           NVARCHAR(50)        NOT NULL,
    predicted_at            DATETIME2           NOT NULL DEFAULT GETUTCDATE(),
    
    CONSTRAINT FK_Predictions_Appointments FOREIGN KEY (appointmentid) REFERENCES dbo.Appointments(appointmentid)
);

CREATE INDEX IX_Predictions_Appointment ON dbo.Predictions(appointmentid);
CREATE INDEX IX_Predictions_Probability ON dbo.Predictions(no_show_probability DESC);
GO

-- -----------------------------------------------------------------------------
-- View: Appointments with computed columns
-- -----------------------------------------------------------------------------
CREATE VIEW dbo.vw_AppointmentsWithMetrics AS
SELECT 
    a.*,
    DATEDIFF(DAY, a.appointmentscheduleddatetime, a.appointmentdatetime) AS lead_time_days,
    DATEPART(WEEKDAY, a.appointmentdate) AS day_of_week,
    DATEPART(HOUR, a.appointmentdatetime) AS hour_of_day,
    CASE WHEN a.appointmentdatetime < GETUTCDATE() THEN 1 ELSE 0 END AS is_past,
    p.patient_gender,
    p.patient_age_bucket,
    p.patient_zip_code,
    pr.provider_specialty,
    pr.providertype,
    d.departmentspecialty,
    d.placeofservicetype,
    d.market,
    i.sipg2
FROM dbo.Appointments a
INNER JOIN dbo.Patients p ON a.patientid = p.patientid
INNER JOIN dbo.Providers pr ON a.providerid = pr.providerid
INNER JOIN dbo.Departments d ON a.departmentid = d.departmentid
LEFT JOIN dbo.Insurance i ON a.primarypatientinsuranceid = i.primarypatientinsuranceid;
GO
