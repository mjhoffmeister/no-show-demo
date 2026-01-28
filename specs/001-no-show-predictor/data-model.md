# Data Model: Medical Appointment No-Show Predictor

**Feature Branch**: `001-no-show-predictor`  
**Date**: 2026-01-28  
**Plan**: [plan.md](plan.md)

## Overview

This document defines the data entities, relationships, and validation rules for the no-show predictor system. The model supports synthetic data generation that aligns with common EHR schema patterns (e.g., Athena, Epic), enabling realistic ML training and seamless future integration with real data sources.

**Source Systems**: Practice management and EHR systems (schema based on industry-standard field naming)

---

## Entity Relationship Diagram

```
┌─────────────┐       ┌─────────────────┐       ┌─────────────┐
│   Patient   │──1:N──│   Appointment   │──N:1──│   Provider  │
└─────────────┘       └─────────────────┘       └─────────────┘
       │                     │                         │
       │                     │                         │
       ▼                     ▼                         ▼
┌─────────────┐       ┌─────────────────┐       ┌─────────────┐
│  Insurance  │       │   Prediction    │       │  Department │
└─────────────┘       └─────────────────┘       └─────────────┘
```

---

## Entities

### Patient

Represents a patient with demographic and behavioral attributes.

| Field | Type | Source Field | Constraints | Description |
|-------|------|--------------|-------------|-------------|
| `patientid` | integer | `patientid` | PK | Unique patient identifier |
| `enterpriseid` | integer | `enterpriseid` | Unique | MRN equivalent in Epic |
| `patient_gender` | string | `patient_gender` | Required: M, F, Other | Patient sex |
| `patient_age_bucket` | string | Calculated | Required | Age range: 0-17, 18-39, 40-64, 65+ |
| `patient_race_ethnicity` | string | `patient_race_ethnicity` | Optional | Race/ethnicity (derived from `patient_race`, `patient_ethnicity`) |
| `patient_email` | string | `patient_email` | Optional, email format | Contact email (synthetic) |
| `patient_zip_code` | string | `patient_zip_code` | Optional, 5-digit | Patient address zip code |
| `portal_enterpriseid` | numeric | `portal_enterpriseid` | Optional | Portal enterprise ID |
| `portal_last_login` | datetime | `portal_last_login` | Optional | Last patient portal login |

**Computed Properties**:
- `patient_age_bucket`: Derived from date of birth
- `historical_no_show_count`: Count of past appointments with status indicating no-show
- `historical_no_show_rate`: `historical_no_show_count / total_past_appointments`
- `portal_engaged`: `portal_last_login` within last 90 days

**Validation Rules**:
- `patient_age_bucket` must be one of defined buckets
- `patient_zip_code` must be 5 digits

---

### Provider

Represents a healthcare provider.

| Field | Type | Source Field | Constraints | Description |
|-------|------|--------------|-------------|-------------|
| `providerid` | integer | `providerid` | PK | Unique provider identifier |
| `pro_providerid` | integer | `pro_providerid` | Optional | Source system provider ID |
| `providerfirstname` | string | `providerfirstname` | Required | Provider first name |
| `providerlastname` | string | `providerlastname` | Required | Provider last name |
| `providertype` | string | `providertype` | Required | Type: Physician, NP, PA, etc. |
| `providertypecategory` | string | `providertypecategory` | Optional | Provider type category |
| `provider_specialty` | string | `provider_specialty` | Required | Medical specialty |
| `provider_specialty_service_line` | string | `provider_specialty_service_line` | Optional | Service line mapping |
| `providernpinumber` | string | `providernpinumber` | Optional | NPI number |
| `provider_affiliation` | string | `provider_affiliation` | Optional | Employed, Affiliated, etc. |
| `entitytype` | string | `entitytype` | Optional | Provider entity type |
| `billableyn` | string | `billableyn` | Optional | Billable flag (Y/N) |
| `patientfacingname` | string | `patientfacingname` | Optional | Patient-facing display name |

**Computed Properties**:
- `display_name`: `{providerfirstname} {providerlastname}, {provider_specialty}`
- `reportingname`: Full name for reporting (from source)
- `schedulingname`: Name used in scheduling

---

### Department

Represents a clinic/department location.

| Field | Type | Source Field | Constraints | Description |
|-------|------|--------------|-------------|-------------|
| `departmentid` | integer | `departmentid` | PK | Unique department identifier |
| `departmentname` | string | `departmentname` | Required | Department name |
| `departmentspecialty` | string | `departmentspecialty` | Optional | Department specialty |
| `billingname` | string | `billingname` | Optional | Billing name |
| `placeofservicecode` | string | `placeofservicecode` | Optional | Place of service code |
| `placeofservicetype` | string | `placeofservicetype` | Optional | Office, Telehealth, etc. |
| `providergroupid` | integer | `providergroupid` | Optional | Provider group ID |
| `departmentgroup` | string | `departmentgroup` | Optional | Department group |
| `contextid` | integer | `contextid` | Optional | Context/org ID |
| `contextname` | string | `contextname` | Optional | Context name (Region A, Region B) |
| `market` | string | `market` | Optional | Market region |
| `division` | string | `division` | Optional | Division/business group |
| `business_unit` | string | `business_unit` | Optional | Business unit |

---

### Appointment

Represents a scheduled medical appointment. Maps to EHR appointment source tables.

| Field | Type | Source Field | Constraints | Description |
|-------|------|--------------|-------------|-------------|
| `appointmentid` | integer | `appointmentid` | PK | Unique appointment identifier |
| `parentappointmentid` | integer | `parentappointmentid` | Optional | Parent for grouped appointments |
| `patientid` | integer | `patientid` | FK → Patient | Patient reference |
| `providerid` | integer | `providerid` | FK → Provider | Provider reference |
| `departmentid` | integer | `departmentid` | FK → Department | Department reference |
| `referringproviderid` | integer | `referringproviderid` | Optional | Referring provider |
| `referralauthid` | integer | `referralauthid` | Optional | Referral authorization ID |
| `appointmentdate` | date | `appointmentdate` | Required | Scheduled date |
| `appointmentstarttime` | string | `appointmentstarttime` | Required | Start time (HH:MM format) |
| `appointmentdatetime` | datetime | Calculated | Required | Combined date and time |
| `appointmentduration` | integer | `appointmentduration` | Required | Duration in minutes |
| `appointmenttypeid` | integer | `appointmenttypeid` | Required | Appointment type ID |
| `appointmenttypename` | string | `appointmenttypename` | Required | Appointment type name |
| `appointmentstatus` | string | `appointmentstatus` | Required | Current status |
| `appointmentcreateddatetime` | datetime | `appointmentcreateddatetime` | Required | When created |
| `appointmentcreatedby` | string | `appointmentcreatedby` | Optional | Created by user |
| `appointmentscheduleddatetime` | datetime | `appointmentscheduleddatetime` | Required | When scheduled |
| `scheduledby` | string | `scheduledby` | Optional | Scheduled by user |
| `appointmentcheckindatetime` | datetime | `appointmentcheckindatetime` | Optional | Check-in time |
| `appointmentcheckoutdatetime` | datetime | `appointmentcheckoutdatetime` | Optional | Check-out time |
| `appointmentcancelleddatetime` | datetime | `appointmentcancelleddatetime` | Optional | Cancellation time |
| `cancelledby` | string | `cancelledby` | Optional | Cancelled by user |
| `appointmentcancelreason` | string | `appointmentcancelreason` | Optional | Cancellation reason |
| `rescheduledappointmentid` | integer | `rescheduledappointmentid` | Optional | Original appointment if rescheduled |
| `rescheduleddatetime` | datetime | `rescheduleddatetime` | Optional | Reschedule timestamp |
| `rescheduledby` | string | `rescheduledby` | Optional | Rescheduled by user |
| `startcheckindatetime` | datetime | `startcheckindatetime` | Optional | Check-in process start |
| `stopsignoffdatetime` | datetime | `stopsignoffdatetime` | Optional | Sign-off process stop |
| `appointmentdeleteddatetime` | datetime | `appointmentdeleteddatetime` | Optional | Deletion timestamp |
| `claimid` | integer | `claimid` | Optional | Associated claim ID |
| `cycletime` | numeric | `cycletime` | Optional | Scheduling to appointment time |
| `frozenyn` | string | `frozenyn` | Optional | Frozen flag (Y/N) |
| `appointmentfrozenreason` | string | `appointmentfrozenreason` | Optional | Reason for frozen |
| `virtual_flag` | string | Calculated | Required | Virtual-Telephone, Virtual-Video, Non-Virtual |
| `new_patient_flag` | string | Calculated | Required | NEW PATIENT or EST PATIENT |
| `webschedulableyn` | integer | `webschedulableyn` | Optional | Online schedulable (1/0) |

**Computed Properties**:
- `appointmentdatetime`: Combined from `appointmentdate` + `appointmentstarttime`
- `lead_time_days`: Days between `appointmentscheduleddatetime` and `appointmentdatetime`
- `day_of_week`: Extracted from `appointmentdate` (Monday=0...Sunday=6)
- `hour_of_day`: Extracted from `appointmentstarttime` (0-23)
- `is_past`: `appointmentdatetime < now`
- `virtual_flag`: Derived from `appointmenttypename` containing VIRTUAL, TELEHEALTH, or PHONE
- `new_patient_flag`: Derived from `appointmenttypename` containing NEW or CONSULT
- `no_show`: Derived from `appointmentstatus` and check-in timestamps

**Status Values** (from `appointmentstatus`):
- `Scheduled` - Future appointment
- `Checked In` - Patient arrived
- `Checked Out` / `Complete` - Visit completed
- `Cancelled` - Appointment cancelled
- `No Show` - Patient did not attend
- `Rescheduled` - Moved to different time

**Validation Rules**:
- `appointmentscheduleddatetime` must be ≤ `appointmentdatetime`
- `appointmentdatetime` must be during business hours (configurable)

---

### Insurance

Represents patient insurance information. Derived from appointment and encounter data.

| Field | Type | Source Field | Constraints | Description |
|-------|------|--------------|-------------|-------------|
| `primarypatientinsuranceid` | integer | `primarypatientinsuranceid` | PK | Primary insurance ID |
| `sipg1` | string | `sipg1` | Optional | Specific payer grouping |
| `sipg2` | string | `sipg2` | Optional | General payer grouping |
| `insurance_plan_1_company_description` | string | `insurance_plan_1_company_description` | Optional | Insurance company name |
| `insurance_group_id` | string | `insurance_group_id` | Optional | Insurance group ID |

---

### Prediction

Represents an ML model prediction for an appointment.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `prediction_id` | string | PK, UUID format | Unique identifier |
| `appointmentid` | integer | FK → Appointment, Required | Appointment reference |
| `no_show_probability` | decimal | Required, 0.0-1.0 | Model confidence |
| `risk_level` | enum | Computed: Low, Medium, High | Categorized risk |
| `risk_factors` | array[RiskFactor] | Required | Contributing factors |
| `model_version` | string | Required | Model version used |
| `predicted_at` | datetime | Auto-generated | When prediction was made |

**Computed Properties**:
- `risk_level` calculation:
  - `Low`: probability < 0.3
  - `Medium`: probability 0.3-0.6
  - `High`: probability > 0.6

---

### RiskFactor

Represents a contributing factor to a prediction (embedded in Prediction).

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `factor_name` | string | Required | Feature name |
| `factor_value` | string | Required | Actual value |
| `contribution` | decimal | Required, 0.0-1.0 | SHAP-like importance |
| `direction` | enum | Required: Increases, Decreases | Effect on risk |

**Example**:
```json
{
  "factor_name": "previous_no_shows",
  "factor_value": "3",
  "contribution": 0.45,
  "direction": "Increases"
}
```

---

### Recommendation

Represents a suggested action based on prediction analysis (agent-generated).

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `recommendation_id` | string | PK, UUID format | Unique identifier |
| `appointment_id` | string | FK → Appointment, Required | Related appointment |
| `action_type` | enum | Required: ConfirmationCall, Reminder, Overbook, NoAction | Suggested action |
| `priority` | enum | Required: Urgent, High, Medium, Low | Action priority |
| `rationale` | string | Required | Explanation for recommendation |
| `created_at` | datetime | Auto-generated | When generated |

---

## Feature Engineering View

Features extracted for ML model training (derived from production schema):

| Feature | Type | Source | Production Field(s) | Description |
|---------|------|--------|---------------------|-------------|
| `patient_age_bucket` | cat | Patient | `patient_age_bucket` (calculated) | Age range category |
| `patient_gender` | cat | Patient | `patient_gender` | Gender category |
| `patient_zip_code` | cat | Patient | `patient_zip_code` | Geographic indicator |
| `patient_race_ethnicity` | cat | Patient | `patient_race_ethnicity` | Demographics |
| `portal_engaged` | bool | Patient | `portal_last_login` | Portal activity in 90 days |
| `sipg2` | cat | Insurance | `sipg2` | General payer grouping |
| `historical_no_show_rate` | float | Calculated | Appointment history | Past no-show percentage |
| `historical_no_show_count` | int | Calculated | Appointment history | Past no-show count |
| `lead_time_days` | int | Appointment | `appointmentscheduleddatetime`, `appointmentdatetime` | Days scheduled ahead |
| `appointmenttypename` | cat | Appointment | `appointmenttypename` | Visit type name |
| `virtual_flag` | cat | Appointment | Calculated from type | Virtual/Non-Virtual |
| `new_patient_flag` | cat | Appointment | Calculated from type | New vs established |
| `day_of_week` | cat | Appointment | `appointmentdate` | Day of week |
| `hour_of_day` | int | Appointment | `appointmentstarttime` | Hour of appointment |
| `appointmentduration` | int | Appointment | `appointmentduration` | Duration in minutes |
| `providerid` | cat | Appointment | `providerid` | Provider (encoded) |
| `provider_specialty` | cat | Provider | `provider_specialty` | Provider specialty |
| `providertype` | cat | Provider | `providertype` | Physician, NP, PA |
| `departmentspecialty` | cat | Department | `departmentspecialty` | Department specialty |
| `placeofservicetype` | cat | Department | `placeofservicetype` | Office, Telehealth |
| `market` | cat | Department | `market` | Geographic market |
| `webschedulableyn` | bool | Appointment | `webschedulableyn` | Online scheduled |
| `cycletime` | float | Appointment | `cycletime` | Schedule to appt time |

**Target Variable**: Derived `no_show` boolean from `appointmentstatus` (No Show status or no check-in for past appointments)

---

## State Transitions

### Appointment Status (from `appointmentstatus` field)

```
                          ┌──────────────┐
                  ┌───────│   Scheduled  │───────┐
                  │       └──────────────┘       │
                  │              │               │
         cancel() │    checkin() │      time passes +
                  │              │      no checkin
                  ▼              ▼               ▼
┌─────────────┐   ┌──────────────┐     ┌─────────────┐
│  Cancelled  │   │  Checked In  │     │   No Show   │
└─────────────┘   └──────────────┘     └─────────────┘
                         │
                checkout()|
                         ▼
                  ┌──────────────┐
                  │   Complete   │
                  └──────────────┘
```

---

## Data Generation Rules

For synthetic data generation, matching production schema patterns:

### No-Show Probability Factors (Evidence-Based)

1. **Higher no-show probability when**:
   - Patient has history of no-shows (strongest predictor, +0.25)
   - `lead_time_days` > 14 days (+0.10)
   - `sipg2` = Medicaid or Self-Pay (+0.08)
   - Monday or Friday appointments (+0.05)
   - `new_patient_flag` = 'NEW PATIENT' (+0.05)
   - `portal_engaged` = false (+0.04)
   - `hour_of_day` in afternoon (14:00-16:00) (+0.03)

2. **Lower no-show probability when**:
   - Patient has 100% attendance history (-0.15)
   - `lead_time_days` < 3 days (-0.08)
   - Early morning appointments (07:00-09:00) (-0.05)
   - `virtual_flag` = 'Virtual-Video' (-0.05)
   - `webschedulableyn` = 1 (self-scheduled) (-0.03)

3. **Base no-show rate**: ~22% (matches typical outpatient)

### Patient Journey Patterns

Generate realistic care sequences per patient:

| Journey Type | Pattern | Frequency |
|--------------|---------|----------|
| **Routine Care** | Annual wellness → follow-up PRN | 40% of patients |
| **Chronic Management** | Initial → monthly/quarterly follow-ups (6-12 visits/year) | 25% of patients |
| **Episodic** | Single visit or 2-3 visit episode, then gap | 20% of patients |
| **Referral Chain** | PCP visit → specialist referral → specialist follow-up | 10% of patients |
| **Care Abandonment** | 2-4 appointments → no-show → no-show → drops out | 5% of patients |

**Journey Generation Rules**:
- Each patient has 5-50 appointments over the 24-month window (avg ~20)
- Follow-up appointments reference prior visit via `parentappointmentid` or scheduling patterns
- Care abandonment journeys show escalating no-show probability (50% → 70% → 90%)
- Referral chains link PCP departments to specialists via `referringproviderid`

### Seasonality Patterns

| Period | No-Show Modifier | Notes |
|--------|------------------|-------|
| Dec 20-Jan 5 | +15% | Holiday season |
| Jul 1-Aug 15 | +8% | Summer vacation |
| Mon after long weekend | +10% | Extended weekends |
| Week before school starts | +5% | Back-to-school chaos |
| Tax season (Apr 1-15) | +3% | Competing priorities |

### Synthetic Data Distributions

| Field | Distribution | Notes |
|-------|-------------|-------|
| `patient_age_bucket` | 15% 0-17, 30% 18-39, 35% 40-64, 20% 65+ | Typical outpatient |
| `patient_gender` | 55% F, 44% M, 1% Other | Primary care skew |
| `sipg2` | 40% Commercial, 25% Medicare, 20% Medicaid, 15% Self-Pay | Payer mix |
| `virtual_flag` | 70% Non-Virtual, 20% Video, 10% Phone | Post-COVID typical |
| `new_patient_flag` | 25% NEW, 75% EST | Established patient majority |
| `lead_time_days` | Gamma(shape=2, scale=7), clipped 0-90 | Right-skewed |
| `appointmentduration` | 15 (40%), 30 (35%), 45 (15%), 60 (10%) | Standard slots |

---

## Indexes and Query Patterns

### Primary Query Patterns

| Query | Entities | Expected Frequency |
|-------|----------|-------------------|
| Appointments by date range | Appointment | High |
| Appointments by patient | Appointment, Patient | Medium |
| Appointments by provider and date | Appointment, Provider | Medium |
| High-risk appointments (prediction > threshold) | Appointment, Prediction | High |
| Patient history | Patient, Appointment | Medium |
| Department schedule | Appointment, Department | Medium |

### Suggested Indexes

```sql
-- Appointment queries (matches production patterns)
CREATE INDEX idx_appointment_date ON appointments(appointmentdate);
CREATE INDEX idx_appointment_datetime ON appointments(appointmentdatetime);
CREATE INDEX idx_appointment_patient ON appointments(patientid);
CREATE INDEX idx_appointment_provider_date ON appointments(providerid, appointmentdate);
CREATE INDEX idx_appointment_department ON appointments(departmentid, appointmentdate);
CREATE INDEX idx_appointment_status ON appointments(appointmentstatus);

-- Prediction queries  
CREATE INDEX idx_prediction_appointment ON predictions(appointmentid);
CREATE INDEX idx_prediction_probability ON predictions(no_show_probability DESC);

-- Patient queries
CREATE INDEX idx_patient_enterprise ON patients(enterpriseid);
```

---

## Schema Mapping Reference

### EHR Field Mapping Notes

Common field mappings between practice management and EHR systems:

| PM Field | EHR Field | Notes |
|--------------|------------|-------|
| `appointmentid` | `parentappointmentid` | Epic uses parent ID for grouping |
| `patientid` | `patient_id` | Direct mapping |
| `enterpriseid` | MRN | MRN equivalent in Epic |
| `providerid` | Shows provider name | Epic shows name, not ID |
| `referringproviderid` | Shows provider name | Epic shows name, not ID |
| `appointmenttypeid` | `appointmenttypename` | Epic uses name, not ID |
| `primarypatientinsuranceid` | Insurance class name | Epic shows high-level grouping |

### Context/Market Fields

| Field | Usage |
|-------|-------|
| `contextname` | Delineates market regions |
| `market` | Geographic market identifier |
| `division` | Organizational division |
| `business_unit` | Custom department grouping |

---

## Data Volume Estimates

| Entity | Count | Notes |
|--------|-------|-------|
| Patient | 5,000 | Synthetic patients with realistic distributions |
| Provider | 100 | Various specialties and types |
| Department | 40 | Mix of specialties and locations |
| Appointment | 100,000 | 24 months history + 2 weeks future (captures seasonality) |
| Insurance Records | 5,000 | Linked to patients |
| Prediction | ~2,000 | Generated on-demand for future appointments |
| Recommendation | ~500 | Generated per agent session |

---

## Version History

| Version | Date | Changes |
|---------|------|---------|  
| 1.0 | 2026-01-28 | Initial data model |
| 2.0 | 2026-01-28 | Aligned with common EHR schema patterns |
