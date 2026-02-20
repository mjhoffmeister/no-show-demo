"""Synthetic data generation for the no-show predictor ML system.

Generates realistic medical appointment data including patient journeys,
seasonality patterns, and evidence-based no-show probability factors.

Data volumes (from spec):
- 5,000 patients with demographics distributions
- 100 providers across specialties
- 40 departments/clinics
- 100,000 appointments over 24+ months
- 5,000 insurance records
"""

import random
from dataclasses import asdict
from datetime import date, datetime, timedelta, timezone
from pathlib import Path
from typing import Any

import numpy as np
import pandas as pd
from faker import Faker

from .schema import (
    AgeBucket,
    Appointment,
    AppointmentStatus,
    Department,
    Gender,
    Insurance,
    NewPatientFlag,
    Patient,
    PayerGrouping,
    PlaceOfServiceType,
    Provider,
    ProviderType,
    VirtualFlag,
)

# Initialize Faker for realistic names
fake = Faker()
Faker.seed(42)
np.random.seed(42)
random.seed(42)


# =============================================================================
# Configuration Constants (from data-model.md specifications)
# =============================================================================

# Patient distributions
AGE_BUCKET_WEIGHTS = {
    AgeBucket.PEDIATRIC: 0.15,
    AgeBucket.YOUNG_ADULT: 0.30,
    AgeBucket.MIDDLE_AGED: 0.35,
    AgeBucket.SENIOR: 0.20,
}

GENDER_WEIGHTS = {
    Gender.FEMALE: 0.55,
    Gender.MALE: 0.44,
    Gender.OTHER: 0.01,
}

PAYER_WEIGHTS = {
    PayerGrouping.COMMERCIAL: 0.40,
    PayerGrouping.MEDICARE: 0.25,
    PayerGrouping.MEDICAID: 0.20,
    PayerGrouping.SELF_PAY: 0.15,
}

# Appointment distributions
VIRTUAL_FLAG_WEIGHTS = {
    VirtualFlag.NON_VIRTUAL: 0.70,
    VirtualFlag.VIRTUAL_VIDEO: 0.20,
    VirtualFlag.VIRTUAL_TELEPHONE: 0.10,
}

NEW_PATIENT_FLAG_WEIGHTS = {
    NewPatientFlag.NEW_PATIENT: 0.25,
    NewPatientFlag.ESTABLISHED: 0.75,
}

DURATION_WEIGHTS = {
    15: 0.40,
    30: 0.35,
    45: 0.15,
    60: 0.10,
}

# Patient journey patterns
JOURNEY_TYPE_WEIGHTS = {
    "routine_care": 0.40,
    "chronic_management": 0.25,
    "episodic": 0.20,
    "referral_chain": 0.10,
    "care_abandonment": 0.05,
}

# No-show base rate calibrated to achieve ~20-22% overall rate
# after applying feature-based modifiers matching Kaggle real-world patterns
# Note: Lead time is the dominant factor - most appointments are 7-30 days out
BASE_NO_SHOW_RATE = -0.02

# Seasonality modifiers
SEASONALITY_MODIFIERS = {
    "holiday_season": {"start": (12, 20), "end": (1, 5), "modifier": 0.15},
    "summer_vacation": {"start": (7, 1), "end": (8, 15), "modifier": 0.08},
    "back_to_school": {"start": (8, 15), "end": (8, 31), "modifier": 0.05},
    "tax_season": {"start": (4, 1), "end": (4, 15), "modifier": 0.03},
}

# Provider specialties
SPECIALTIES = [
    "Family Medicine",
    "Internal Medicine",
    "Pediatrics",
    "Cardiology",
    "Orthopedics",
    "Dermatology",
    "Neurology",
    "Gastroenterology",
    "Endocrinology",
    "Pulmonology",
    "OB/GYN",
    "Psychiatry",
    "Rheumatology",
    "Urology",
    "Ophthalmology",
]

# Appointment types by specialty
APPOINTMENT_TYPES = {
    "primary_care": [
        ("Annual Wellness Exam", 30),
        ("Follow-up Visit", 15),
        ("New Patient Visit", 45),
        ("Sick Visit", 15),
        ("Preventive Care", 30),
    ],
    "specialty": [
        ("Consultation", 45),
        ("Follow-up Visit", 30),
        ("Procedure", 60),
        ("New Patient Eval", 60),
    ],
    "telehealth": [
        ("Virtual Follow-up", 15),
        ("Video Visit", 30),
        ("Phone Consultation", 15),
    ],
}

# Insurance companies by payer group
INSURANCE_COMPANIES = {
    PayerGrouping.COMMERCIAL: [
        "Blue Cross Blue Shield",
        "United Healthcare",
        "Aetna",
        "Cigna",
        "Humana",
    ],
    PayerGrouping.MEDICARE: [
        "Medicare Part B",
        "Medicare Advantage",
        "Medicare Supplement",
    ],
    PayerGrouping.MEDICAID: [
        "Medicaid",
        "Managed Medicaid Plan",
    ],
    PayerGrouping.SELF_PAY: [
        "Self-Pay",
        "Uninsured",
    ],
}

# Market regions for departments
MARKETS = ["Region A", "Region B", "Region C", "Region D"]


# =============================================================================
# Weighted Random Selection Helpers
# =============================================================================


def weighted_choice(options: dict) -> Any:
    """Select an option based on weighted probabilities."""
    items = list(options.keys())
    weights = list(options.values())
    return random.choices(items, weights=weights, k=1)[0]


def gamma_lead_time() -> int:
    """Generate lead time using gamma distribution, clipped 0-90 days."""
    value = np.random.gamma(shape=2, scale=7)
    return int(np.clip(value, 0, 90))


# =============================================================================
# Entity Generators
# =============================================================================


def generate_patients(count: int = 5000) -> list[Patient]:
    """Generate synthetic patient records with realistic distributions."""
    patients = []

    # Pre-generate zip codes from diverse regions
    zip_codes = [
        str(random.randint(10001, 99999)).zfill(5)
        for _ in range(100)
    ]

    race_ethnicity_options = [
        "White",
        "Black or African American",
        "Hispanic or Latino",
        "Asian",
        "American Indian",
        "Native Hawaiian",
        "Two or More Races",
        "Other",
        None,  # Unknown/declined
    ]
    race_weights = [0.60, 0.13, 0.12, 0.06, 0.01, 0.01, 0.03, 0.02, 0.02]

    for patient_id in range(1, count + 1):
        gender = weighted_choice(GENDER_WEIGHTS)
        age_bucket = weighted_choice(AGE_BUCKET_WEIGHTS)

        # Generate portal engagement (70% have portal access, 50% of those are active)
        has_portal = random.random() < 0.70
        portal_login = None
        if has_portal:
            # Active users logged in recently, inactive users longer ago
            if random.random() < 0.50:
                days_ago = random.randint(1, 60)  # Active
            else:
                days_ago = random.randint(91, 365)  # Inactive
            portal_login = datetime.now(timezone.utc) - timedelta(days=days_ago)

        patient = Patient(
            patientid=patient_id,
            enterpriseid=1000000 + patient_id,
            patient_gender=gender,
            patient_age_bucket=age_bucket,
            patient_race_ethnicity=random.choices(race_ethnicity_options, weights=race_weights)[0],
            patient_email=fake.email() if random.random() < 0.80 else None,
            patient_zip_code=random.choice(zip_codes),
            portal_enterpriseid=patient_id if has_portal else None,
            portal_last_login=portal_login,
            historical_no_show_count=0,
            historical_no_show_rate=0.0,
        )
        patients.append(patient)

    return patients


def generate_providers(count: int = 100) -> list[Provider]:
    """Generate synthetic provider records across specialties."""
    providers = []

    # Define provider type distribution
    provider_type_weights = {
        ProviderType.PHYSICIAN: 0.60,
        ProviderType.NURSE_PRACTITIONER: 0.25,
        ProviderType.PHYSICIAN_ASSISTANT: 0.15,
    }

    affiliations = ["Employed", "Affiliated", "Locum Tenens"]

    for provider_id in range(1, count + 1):
        provider_type = weighted_choice(provider_type_weights)
        specialty = random.choice(SPECIALTIES)

        # Use Faker for names
        first_name = fake.first_name()
        last_name = fake.last_name()

        provider = Provider(
            providerid=provider_id,
            pro_providerid=provider_id + 10000,
            providerfirstname=first_name,
            providerlastname=last_name,
            providertype=provider_type,
            providertypecategory=provider_type.value,
            provider_specialty=specialty,
            provider_specialty_service_line=specialty,
            providernpinumber=str(random.randint(1000000000, 9999999999)),
            provider_affiliation=random.choice(affiliations),
            entitytype="Individual",
            billableyn="Y",
            patientfacingname=f"Dr. {first_name} {last_name}",
        )
        providers.append(provider)

    return providers


def generate_departments(count: int = 40) -> list[Department]:
    """Generate synthetic department records."""
    departments = []

    place_of_service_types = [
        PlaceOfServiceType.OFFICE,
        PlaceOfServiceType.TELEHEALTH,
        PlaceOfServiceType.URGENT_CARE,
    ]
    pos_weights = [0.85, 0.10, 0.05]

    for dept_id in range(1, count + 1):
        specialty = random.choice(SPECIALTIES)
        market = random.choice(MARKETS)
        pos_type = random.choices(place_of_service_types, weights=pos_weights)[0]

        department = Department(
            departmentid=dept_id,
            departmentname=f"{specialty} - {market}",
            departmentspecialty=specialty,
            billingname=f"{specialty} Clinic",
            placeofservicecode="11" if pos_type == PlaceOfServiceType.OFFICE else "02",
            placeofservicetype=pos_type,
            providergroupid=dept_id // 5 + 1,
            departmentgroup=specialty,
            contextid=MARKETS.index(market) + 1,
            contextname=market,
            market=market,
            division="Primary Care" if specialty in ["Family Medicine", "Internal Medicine", "Pediatrics"] else "Specialty",
            business_unit="Ambulatory",
        )
        departments.append(department)

    return departments


def generate_insurance(patients: list[Patient]) -> list[Insurance]:
    """Generate insurance records linked to patients."""
    insurance_records = []

    for patient in patients:
        payer_group = weighted_choice(PAYER_WEIGHTS)
        company = random.choice(INSURANCE_COMPANIES[payer_group])

        insurance = Insurance(
            primarypatientinsuranceid=patient.patientid,
            patientid=patient.patientid,
            sipg1=company.replace(" ", "_").upper()[:10],
            sipg2=payer_group,
            insurance_plan_1_company_description=company,
            insurance_group_id=str(random.randint(100000, 999999)) if payer_group != PayerGrouping.SELF_PAY else None,
        )
        insurance_records.append(insurance)

    return insurance_records


# =============================================================================
# No-Show Probability Calculation
# =============================================================================


def calculate_no_show_probability(
    patient: Patient,
    appointment_date: date,
    lead_time_days: int,
    hour_of_day: int,
    day_of_week: int,
    virtual_flag: VirtualFlag,
    new_patient_flag: NewPatientFlag,
    payer_group: PayerGrouping,
    web_scheduled: bool,
    journey_stage: str,
) -> float:
    """Calculate no-show probability based on evidence-based factors.

    Calibrated against Kaggle Medical Appointment No-Shows dataset patterns:
    - Lead time is strongest predictor (corr +0.19 in real data)
    - Age has moderate effect (younger patients no-show more)
    - Patient history has strong predictive value
    - Payer type, day of week, time of day have smaller effects

    Target correlations (matching Kaggle):
    - lead_time_days: +0.15 to +0.20
    - age: -0.05 to -0.07
    - historical_no_show_rate: +0.10 to +0.15
    """
    probability = BASE_NO_SHOW_RATE  # 0.18

    # ==========================================================================
    # LEAD TIME - Strongest predictor (Kaggle: corr +0.186)
    # Real data shows: same-day ~7%, 1-7d ~25%, 8-14d ~31%, 15-30d ~33%, 30d+ ~33%
    # ==========================================================================
    if lead_time_days <= 0:
        probability -= 0.12  # Same-day appointments have very low no-show
    elif lead_time_days <= 3:
        probability -= 0.06  # Short lead time = lower no-show
    elif lead_time_days <= 7:
        probability += 0.04  # ~1 week out
    elif lead_time_days <= 14:
        probability += 0.10  # ~2 weeks out
    elif lead_time_days <= 30:
        probability += 0.14  # ~1 month out
    else:
        probability += 0.16  # 30+ days - highest risk

    # ==========================================================================
    # AGE - Moderate predictor (Kaggle: corr -0.060)
    # Real data: 0-18 (22.5%), 18-40 (23.2%), 40-65 (17.9%), 65+ (15.5%)
    # ==========================================================================
    if patient.patient_age_bucket == AgeBucket.PEDIATRIC:
        probability += 0.03  # Kids: parents forget
    elif patient.patient_age_bucket == AgeBucket.YOUNG_ADULT:
        probability += 0.04  # Young adults: highest no-show
    elif patient.patient_age_bucket == AgeBucket.MIDDLE_AGED:
        probability -= 0.02  # Middle-aged: more reliable
    elif patient.patient_age_bucket == AgeBucket.SENIOR:
        probability -= 0.05  # Seniors: most reliable

    # ==========================================================================
    # PATIENT HISTORY - Strong predictor when available
    # ==========================================================================
    if patient.historical_no_show_count > 0:
        # Scale impact by historical rate, capped to avoid runaway values
        history_impact = min(0.20, patient.historical_no_show_rate * 0.5)
        probability += history_impact
    elif patient.historical_no_show_count == 0 and hasattr(patient, '_has_history') and patient._has_history:
        # Perfect attendance bonus
        probability -= 0.08

    # ==========================================================================
    # PAYER TYPE - Moderate predictor
    # ==========================================================================
    if payer_group == PayerGrouping.MEDICAID:
        probability += 0.06
    elif payer_group == PayerGrouping.SELF_PAY:
        probability += 0.08  # Highest risk
    elif payer_group == PayerGrouping.MEDICARE:
        probability -= 0.03  # Seniors, more reliable

    # ==========================================================================
    # DAY OF WEEK - Minor predictor
    # Monday=0, Friday=4 tend to have higher no-shows
    # ==========================================================================
    if day_of_week == 0:  # Monday
        probability += 0.03
    elif day_of_week == 4:  # Friday
        probability += 0.02

    # ==========================================================================
    # TIME OF DAY - Minor predictor
    # ==========================================================================
    if 14 <= hour_of_day <= 16:
        probability += 0.02  # Afternoon slump
    elif 7 <= hour_of_day <= 9:
        probability -= 0.03  # Early morning = committed

    # ==========================================================================
    # APPOINTMENT TYPE FACTORS
    # ==========================================================================
    # New patient
    if new_patient_flag == NewPatientFlag.NEW_PATIENT:
        probability += 0.04

    # Virtual appointments (lower no-show - easier to attend)
    if virtual_flag == VirtualFlag.VIRTUAL_VIDEO:
        probability -= 0.05
    elif virtual_flag == VirtualFlag.VIRTUAL_TELEPHONE:
        probability -= 0.03

    # Portal engagement (proxy for patient engagement)
    if not patient.portal_engaged:
        probability += 0.03

    # Web scheduled (engaged patients)
    if web_scheduled:
        probability -= 0.02

    # ==========================================================================
    # SEASONALITY
    # ==========================================================================
    probability += _get_seasonality_modifier(appointment_date)

    # ==========================================================================
    # JOURNEY STAGE (care abandonment pattern)
    # ==========================================================================
    if journey_stage == "care_abandonment_escalating":
        probability += 0.20

    # Clamp to valid range
    return max(0.03, min(0.85, probability))


def _get_seasonality_modifier(appointment_date: date) -> float:
    """Calculate seasonality modifier for a given date."""
    month = appointment_date.month
    day = appointment_date.day

    for season, config in SEASONALITY_MODIFIERS.items():
        start_month, start_day = config["start"]
        end_month, end_day = config["end"]
        modifier = config["modifier"]

        # Handle year-wrap (e.g., Dec 20 - Jan 5)
        if start_month > end_month:
            if month >= start_month and day >= start_day:
                return modifier
            if month <= end_month and day <= end_day:
                return modifier
        else:
            if start_month <= month <= end_month:
                if (month > start_month or day >= start_day) and (month < end_month or day <= end_day):
                    return modifier

    return 0.0


# =============================================================================
# Appointment Generation with Patient Journeys
# =============================================================================


def generate_appointments(
    patients: list[Patient],
    providers: list[Provider],
    departments: list[Department],
    insurance_records: list[Insurance],
    count: int = 100000,
    start_date: date | None = None,
    end_date: date | None = None,
) -> list[Appointment]:
    """Generate synthetic appointments with patient journey patterns.

    Implements journey types from data-model.md:
    - Routine Care (40%): Annual wellness + PRN follow-ups
    - Chronic Management (25%): Monthly/quarterly visits
    - Episodic (20%): 1-3 visit episodes
    - Referral Chain (10%): PCP -> Specialist
    - Care Abandonment (5%): 2-4 appts -> no-shows -> dropout
    """
    appointments: list[Appointment] = []

    # Calculate date range (24 months history + 6 weeks future)
    if end_date is None:
        end_date = date.today() + timedelta(weeks=6)
    if start_date is None:
        start_date = end_date - timedelta(days=730)  # 24 months

    # Build lookup maps
    insurance_by_patient = {ins.patientid: ins for ins in insurance_records}
    providers_by_specialty: dict[str, list[Provider]] = {}
    for provider in providers:
        specialty = provider.provider_specialty
        if specialty not in providers_by_specialty:
            providers_by_specialty[specialty] = []
        providers_by_specialty[specialty].append(provider)

    departments_by_specialty: dict[str, list[Department]] = {}
    for dept in departments:
        specialty = dept.departmentspecialty or "General"
        if specialty not in departments_by_specialty:
            departments_by_specialty[specialty] = []
        departments_by_specialty[specialty].append(dept)

    # Primary care specialties for referral chains
    primary_care_specialties = ["Family Medicine", "Internal Medicine", "Pediatrics"]

    appointment_id = 1
    appointments_created = 0

    # Assign journey types to patients
    patient_journeys = {}
    for patient in patients:
        patient_journeys[patient.patientid] = weighted_choice(JOURNEY_TYPE_WEIGHTS)

    # Track patient history across multiple passes
    patient_stats: dict[int, dict[str, Any]] = {
        p.patientid: {"no_shows": 0, "total": 0, "last_appt_date": None}
        for p in patients
    }

    # Keep generating until we reach the target count
    # Multiple passes through patients if needed
    pass_number = 0
    while appointments_created < count:
        pass_number += 1
        patients_this_pass = random.sample(patients, len(patients))  # Shuffle for variety
        
        for patient in patients_this_pass:
            if appointments_created >= count:
                break
                
            journey_type = patient_journeys[patient.patientid]
            payer_group = insurance_by_patient.get(patient.patientid)
            payer = payer_group.sipg2 if payer_group else PayerGrouping.SELF_PAY

            # Determine number of appointments for this patient this pass
            # Scale up for subsequent passes to reach target faster
            base_multiplier = 1 + (pass_number - 1) * 0.5
            if journey_type == "routine_care":
                num_appointments = int(random.randint(2, 6) * base_multiplier)
            elif journey_type == "chronic_management":
                num_appointments = int(random.randint(6, 24) * base_multiplier)
            elif journey_type == "episodic":
                num_appointments = int(random.randint(1, 4) * base_multiplier)
            elif journey_type == "referral_chain":
                num_appointments = int(random.randint(3, 8) * base_multiplier)
            else:  # care_abandonment
                num_appointments = int(random.randint(3, 6) * base_multiplier)

            # Track patient's appointment history for no-show calculation
            patient_no_shows = patient_stats[patient.patientid]["no_shows"]
            patient_total = patient_stats[patient.patientid]["total"]
            last_appt_date = patient_stats[patient.patientid]["last_appt_date"]
            parent_appointment_id = None

            for appt_num in range(num_appointments):
                if appointments_created >= count:
                    break

                # Select specialty based on journey type
                if journey_type == "referral_chain":
                    if appt_num == 0:
                        specialty = random.choice(primary_care_specialties)
                    else:
                        specialty = random.choice([s for s in SPECIALTIES if s not in primary_care_specialties])
                else:
                    specialty = random.choice(SPECIALTIES)

                # Select provider and department
                available_providers = providers_by_specialty.get(specialty)
                if not available_providers:
                    available_providers = providers
                provider = random.choice(available_providers)

                available_depts = departments_by_specialty.get(specialty)
                if not available_depts:
                    available_depts = departments
                department = random.choice(available_depts)

                # Generate appointment date within range
                if appt_num == 0 and last_appt_date is None:
                    # First appointment ever: random within range
                    days_from_start = random.randint(0, (end_date - start_date).days - 30)
                    appt_date = start_date + timedelta(days=days_from_start)
                elif appt_num == 0 and last_appt_date is not None:
                    # Continuation from previous pass: after last appointment
                    interval = random.randint(7, 90)
                    appt_date = last_appt_date + timedelta(days=interval)
                    if appt_date > end_date:
                        appt_date = start_date + timedelta(days=random.randint(0, 365))
                else:
                    # Subsequent appointments: after previous with journey-specific intervals
                    if journey_type == "chronic_management":
                        interval = random.randint(21, 90)  # Monthly to quarterly
                    elif journey_type == "care_abandonment":
                        interval = random.randint(14, 45)
                    else:
                        interval = random.randint(7, 180)

                    appt_date = appt_date + timedelta(days=interval)
                    if appt_date > end_date:
                        # Wrap around to fill more dates
                        appt_date = start_date + timedelta(days=random.randint(0, 365))

                # Appointment time (business hours 7:00-17:00)
                hour = random.randint(7, 16)
                minute = random.choice([0, 15, 30, 45])
                appt_time = f"{hour:02d}:{minute:02d}"

                # Virtual flag
                virtual_flag = weighted_choice(VIRTUAL_FLAG_WEIGHTS)

                # New patient flag (based on patient's total history, not just this pass)
                if patient_total == 0 and appt_num == 0:
                    new_patient_flag = NewPatientFlag.NEW_PATIENT
                else:
                    new_patient_flag = NewPatientFlag.ESTABLISHED

                # Duration
                duration = weighted_choice(DURATION_WEIGHTS)

                # Lead time
                lead_time = gamma_lead_time()
                scheduled_datetime = datetime.combine(appt_date, datetime.min.time()) - timedelta(days=lead_time)

                # Web scheduled (more likely for portal-engaged patients)
                web_scheduled = random.random() < (0.40 if patient.portal_engaged else 0.15)

                # Appointment type
                if virtual_flag != VirtualFlag.NON_VIRTUAL:
                    appt_type = random.choice(APPOINTMENT_TYPES["telehealth"])
                elif specialty in primary_care_specialties:
                    appt_type = random.choice(APPOINTMENT_TYPES["primary_care"])
                else:
                    appt_type = random.choice(APPOINTMENT_TYPES["specialty"])

                appt_type_name, _ = appt_type

                # Calculate no-show probability
                journey_stage = "care_abandonment_escalating" if (
                    journey_type == "care_abandonment" and appt_num >= 2
                ) else "normal"

                # Update patient stats for calculation
                patient.historical_no_show_count = patient_no_shows
                patient.historical_no_show_rate = patient_no_shows / max(1, patient_total)

                no_show_prob = calculate_no_show_probability(
                    patient=patient,
                    appointment_date=appt_date,
                    lead_time_days=lead_time,
                    hour_of_day=hour,
                    day_of_week=appt_date.weekday(),
                    virtual_flag=virtual_flag,
                    new_patient_flag=new_patient_flag,
                    payer_group=payer,
                    web_scheduled=web_scheduled,
                    journey_stage=journey_stage,
                )

                # Determine appointment outcome
                is_future = appt_date > date.today()
                is_no_show = False

                if is_future:
                    status = AppointmentStatus.SCHEDULED
                    checkin_time = None
                    checkout_time = None
                else:
                    # Past appointment - determine outcome
                    roll = random.random()
                    if roll < no_show_prob:
                        status = AppointmentStatus.NO_SHOW
                        is_no_show = True
                        checkin_time = None
                        checkout_time = None
                    elif roll < no_show_prob + 0.10:
                        status = AppointmentStatus.CANCELLED
                        checkin_time = None
                        checkout_time = None
                    else:
                        status = AppointmentStatus.COMPLETE
                        appt_datetime = datetime.combine(appt_date, datetime.min.time().replace(hour=hour, minute=minute))
                        checkin_time = appt_datetime - timedelta(minutes=random.randint(5, 30))
                        checkout_time = appt_datetime + timedelta(minutes=duration + random.randint(5, 45))

                # Update patient history
                if not is_future:
                    patient_total += 1
                    if is_no_show:
                        patient_no_shows += 1

                # Create appointment record
                appt_datetime_created = scheduled_datetime - timedelta(days=random.randint(0, 5))

                appointment = Appointment(
                    appointmentid=appointment_id,
                    patientid=patient.patientid,
                    providerid=provider.providerid,
                    departmentid=department.departmentid,
                    appointmentdate=appt_date,
                    appointmentstarttime=appt_time,
                    appointmentduration=duration,
                    appointmenttypeid=hash(appt_type_name) % 1000,
                    appointmenttypename=appt_type_name,
                    appointmentstatus=status,
                    appointmentcreateddatetime=appt_datetime_created,
                    appointmentscheduleddatetime=scheduled_datetime,
                    parentappointmentid=parent_appointment_id,
                    referringproviderid=provider.providerid if journey_type == "referral_chain" and appt_num > 0 else None,
                    appointmentcheckindatetime=checkin_time,
                    appointmentcheckoutdatetime=checkout_time,
                    appointmentcancelleddatetime=scheduled_datetime if status == AppointmentStatus.CANCELLED else None,
                    webschedulableyn=1 if web_scheduled else 0,
                    virtual_flag=virtual_flag,
                    new_patient_flag=new_patient_flag,
                )

                appointments.append(appointment)
                parent_appointment_id = appointment_id
                appointment_id += 1
                appointments_created += 1
            
            # Update patient stats for next pass
            patient_stats[patient.patientid]["no_shows"] = patient_no_shows
            patient_stats[patient.patientid]["total"] = patient_total
            patient_stats[patient.patientid]["last_appt_date"] = appt_date

        if appointments_created >= count:
            break

    # Update final patient stats
    _update_patient_statistics(patients, appointments)

    return appointments


def _update_patient_statistics(patients: list[Patient], appointments: list[Appointment]) -> None:
    """Update patient historical no-show statistics."""
    patient_stats: dict[int, dict[str, int]] = {}

    for appt in appointments:
        if appt.appointmentdate <= date.today():
            if appt.patientid not in patient_stats:
                patient_stats[appt.patientid] = {"total": 0, "no_shows": 0}
            patient_stats[appt.patientid]["total"] += 1
            if appt.no_show:
                patient_stats[appt.patientid]["no_shows"] += 1

    for patient in patients:
        stats = patient_stats.get(patient.patientid, {"total": 0, "no_shows": 0})
        patient.historical_no_show_count = stats["no_shows"]
        patient.historical_no_show_rate = stats["no_shows"] / max(1, stats["total"])


# =============================================================================
# Data Export Functions
# =============================================================================


def entities_to_dataframe(entities: list, exclude_computed: bool = False) -> pd.DataFrame:
    """Convert a list of dataclass entities to a pandas DataFrame."""
    records = []
    for entity in entities:
        record = {}
        for key, value in asdict(entity).items():
            # Convert enums to their values
            if hasattr(value, 'value'):
                value = value.value
            record[key] = value
        records.append(record)
    return pd.DataFrame(records)


def save_to_parquet(
    output_dir: Path,
    patients: list[Patient],
    providers: list[Provider],
    departments: list[Department],
    insurance_records: list[Insurance],
    appointments: list[Appointment],
) -> dict[str, Path]:
    """Save all generated data to parquet files.

    Args:
        output_dir: Directory to save parquet files
        patients: List of Patient entities
        providers: List of Provider entities
        departments: List of Department entities
        insurance_records: List of Insurance entities
        appointments: List of Appointment entities

    Returns:
        Dictionary mapping entity names to file paths
    """
    output_dir = Path(output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)

    files = {}

    # Save patients
    df_patients = entities_to_dataframe(patients)
    path = output_dir / "patients.parquet"
    df_patients.to_parquet(path, index=False)
    files["patients"] = path

    # Save providers
    df_providers = entities_to_dataframe(providers)
    path = output_dir / "providers.parquet"
    df_providers.to_parquet(path, index=False)
    files["providers"] = path

    # Save departments
    df_departments = entities_to_dataframe(departments)
    path = output_dir / "departments.parquet"
    df_departments.to_parquet(path, index=False)
    files["departments"] = path

    # Save insurance
    df_insurance = entities_to_dataframe(insurance_records)
    path = output_dir / "insurance.parquet"
    df_insurance.to_parquet(path, index=False)
    files["insurance"] = path

    # Save appointments
    df_appointments = entities_to_dataframe(appointments)
    path = output_dir / "appointments.parquet"
    df_appointments.to_parquet(path, index=False)
    files["appointments"] = path

    return files


def generate_all_data(
    output_dir: Path | str,
    num_patients: int = 5000,
    num_providers: int = 100,
    num_departments: int = 40,
    num_appointments: int = 100000,
) -> dict[str, Path]:
    """Generate complete synthetic dataset and save to parquet files.

    This is the main entry point for data generation.

    Args:
        output_dir: Directory to save generated data
        num_patients: Number of patients to generate (default: 5000)
        num_providers: Number of providers to generate (default: 100)
        num_departments: Number of departments to generate (default: 40)
        num_appointments: Number of appointments to generate (default: 100000)

    Returns:
        Dictionary mapping entity names to saved file paths
    """
    output_dir = Path(output_dir)

    print(f"Generating {num_patients} patients...")
    patients = generate_patients(num_patients)

    print(f"Generating {num_providers} providers...")
    providers = generate_providers(num_providers)

    print(f"Generating {num_departments} departments...")
    departments = generate_departments(num_departments)

    print(f"Generating {num_patients} insurance records...")
    insurance_records = generate_insurance(patients)

    print(f"Generating {num_appointments} appointments with patient journeys...")
    appointments = generate_appointments(
        patients=patients,
        providers=providers,
        departments=departments,
        insurance_records=insurance_records,
        count=num_appointments,
    )

    print(f"Saving data to {output_dir}...")
    files = save_to_parquet(
        output_dir=output_dir,
        patients=patients,
        providers=providers,
        departments=departments,
        insurance_records=insurance_records,
        appointments=appointments,
    )

    # Print summary
    print("\n=== Generation Summary ===")
    print(f"Patients:     {len(patients):,}")
    print(f"Providers:    {len(providers):,}")
    print(f"Departments:  {len(departments):,}")
    print(f"Insurance:    {len(insurance_records):,}")
    print(f"Appointments: {len(appointments):,}")

    # Calculate no-show rate
    past_appts = [a for a in appointments if a.appointmentdate <= date.today()]
    no_shows = sum(1 for a in past_appts if a.no_show)
    no_show_rate = no_shows / max(1, len(past_appts))
    print(f"\nNo-show rate: {no_show_rate:.1%}")

    return files


# =============================================================================
# CLI Entry Point
# =============================================================================


if __name__ == "__main__":
    import argparse

    parser = argparse.ArgumentParser(description="Generate synthetic medical appointment data")
    parser.add_argument(
        "--output-dir",
        type=str,
        default="./data/synthetic",
        help="Output directory for parquet files",
    )
    parser.add_argument(
        "--patients",
        type=int,
        default=5000,
        help="Number of patients to generate",
    )
    parser.add_argument(
        "--providers",
        type=int,
        default=100,
        help="Number of providers to generate",
    )
    parser.add_argument(
        "--departments",
        type=int,
        default=40,
        help="Number of departments to generate",
    )
    parser.add_argument(
        "--appointments",
        type=int,
        default=100000,
        help="Number of appointments to generate",
    )

    args = parser.parse_args()

    generate_all_data(
        output_dir=args.output_dir,
        num_patients=args.patients,
        num_providers=args.providers,
        num_departments=args.departments,
        num_appointments=args.appointments,
    )
