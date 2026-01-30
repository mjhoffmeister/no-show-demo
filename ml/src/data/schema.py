"""Data schema definitions for the no-show predictor ML system.

This module defines dataclasses matching the data model specification.
All entities align with common EHR schema patterns for production compatibility.
"""

from dataclasses import dataclass, field
from datetime import date, datetime, timezone
from decimal import Decimal
from enum import Enum
from typing import Optional
from uuid import UUID, uuid4


class Gender(str, Enum):
    """Patient gender options."""

    MALE = "M"
    FEMALE = "F"
    OTHER = "Other"


class AgeBucket(str, Enum):
    """Patient age range categories."""

    PEDIATRIC = "0-17"
    YOUNG_ADULT = "18-39"
    MIDDLE_AGED = "40-64"
    SENIOR = "65+"


class ProviderType(str, Enum):
    """Healthcare provider type classifications."""

    PHYSICIAN = "Physician"
    NURSE_PRACTITIONER = "NP"
    PHYSICIAN_ASSISTANT = "PA"
    REGISTERED_NURSE = "RN"
    MEDICAL_ASSISTANT = "MA"


class AppointmentStatus(str, Enum):
    """Appointment status values from EHR systems."""

    SCHEDULED = "Scheduled"
    CHECKED_IN = "Checked In"
    CHECKED_OUT = "Checked Out"
    COMPLETE = "Complete"
    CANCELLED = "Cancelled"
    NO_SHOW = "No Show"
    RESCHEDULED = "Rescheduled"


class VirtualFlag(str, Enum):
    """Appointment modality classification."""

    NON_VIRTUAL = "Non-Virtual"
    VIRTUAL_VIDEO = "Virtual-Video"
    VIRTUAL_TELEPHONE = "Virtual-Telephone"


class NewPatientFlag(str, Enum):
    """Patient type for the appointment."""

    NEW_PATIENT = "NEW PATIENT"
    ESTABLISHED = "EST PATIENT"


class PlaceOfServiceType(str, Enum):
    """Place of service classifications."""

    OFFICE = "Office"
    TELEHEALTH = "Telehealth"
    HOSPITAL = "Hospital"
    URGENT_CARE = "Urgent Care"
    OTHER = "Other"


class RiskLevel(str, Enum):
    """Risk level categorization for predictions."""

    LOW = "Low"
    MEDIUM = "Medium"
    HIGH = "High"


class RiskDirection(str, Enum):
    """Direction of risk factor contribution."""

    INCREASES = "Increases"
    DECREASES = "Decreases"


class ActionType(str, Enum):
    """Recommended action types."""

    CONFIRMATION_CALL = "ConfirmationCall"
    REMINDER = "Reminder"
    OVERBOOK = "Overbook"
    NO_ACTION = "NoAction"


class Priority(str, Enum):
    """Action priority levels."""

    URGENT = "Urgent"
    HIGH = "High"
    MEDIUM = "Medium"
    LOW = "Low"


class PayerGrouping(str, Enum):
    """Insurance payer groupings (SIPG2)."""

    COMMERCIAL = "Commercial"
    MEDICARE = "Medicare"
    MEDICAID = "Medicaid"
    SELF_PAY = "Self-Pay"


@dataclass
class Patient:
    """Represents a patient with demographic and behavioral attributes."""

    patientid: int
    enterpriseid: int
    patient_gender: Gender
    patient_age_bucket: AgeBucket
    patient_race_ethnicity: Optional[str] = None
    patient_email: Optional[str] = None
    patient_zip_code: Optional[str] = None
    portal_enterpriseid: Optional[int] = None
    portal_last_login: Optional[datetime] = None

    # Computed properties stored for ML features
    historical_no_show_count: int = 0
    historical_no_show_rate: float = 0.0

    @property
    def portal_engaged(self) -> bool:
        """Check if patient portal login was within last 90 days."""
        if self.portal_last_login is None:
            return False
        # Handle both naive and aware datetimes
        now = datetime.now(timezone.utc)
        login = self.portal_last_login
        if login.tzinfo is None:
            login = login.replace(tzinfo=timezone.utc)
        days_since_login = (now - login).days
        return days_since_login <= 90


@dataclass
class Provider:
    """Represents a healthcare provider."""

    providerid: int
    providerfirstname: str
    providerlastname: str
    providertype: ProviderType
    provider_specialty: str
    pro_providerid: Optional[int] = None
    providertypecategory: Optional[str] = None
    provider_specialty_service_line: Optional[str] = None
    providernpinumber: Optional[str] = None
    provider_affiliation: Optional[str] = None
    entitytype: Optional[str] = None
    billableyn: Optional[str] = None
    patientfacingname: Optional[str] = None

    @property
    def display_name(self) -> str:
        """Patient-facing display name."""
        return f"{self.providerfirstname} {self.providerlastname}, {self.provider_specialty}"


@dataclass
class Department:
    """Represents a clinic/department location."""

    departmentid: int
    departmentname: str
    departmentspecialty: Optional[str] = None
    billingname: Optional[str] = None
    placeofservicecode: Optional[str] = None
    placeofservicetype: Optional[PlaceOfServiceType] = None
    providergroupid: Optional[int] = None
    departmentgroup: Optional[str] = None
    contextid: Optional[int] = None
    contextname: Optional[str] = None
    market: Optional[str] = None
    division: Optional[str] = None
    business_unit: Optional[str] = None


@dataclass
class Insurance:
    """Represents patient insurance information."""

    primarypatientinsuranceid: int
    patientid: int  # FK to Patient
    sipg1: Optional[str] = None
    sipg2: Optional[PayerGrouping] = None
    insurance_plan_1_company_description: Optional[str] = None
    insurance_group_id: Optional[str] = None


@dataclass
class Appointment:
    """Represents a scheduled medical appointment.

    Maps to EHR appointment source tables with computed properties for ML features.
    """

    appointmentid: int
    patientid: int  # FK to Patient
    providerid: int  # FK to Provider
    departmentid: int  # FK to Department
    appointmentdate: date
    appointmentstarttime: str  # HH:MM format
    appointmentduration: int  # Minutes
    appointmenttypeid: int
    appointmenttypename: str
    appointmentstatus: AppointmentStatus
    appointmentcreateddatetime: datetime
    appointmentscheduleddatetime: datetime
    parentappointmentid: Optional[int] = None
    referringproviderid: Optional[int] = None
    referralauthid: Optional[int] = None
    appointmentcreatedby: Optional[str] = None
    scheduledby: Optional[str] = None
    appointmentcheckindatetime: Optional[datetime] = None
    appointmentcheckoutdatetime: Optional[datetime] = None
    appointmentcancelleddatetime: Optional[datetime] = None
    cancelledby: Optional[str] = None
    appointmentcancelreason: Optional[str] = None
    rescheduledappointmentid: Optional[int] = None
    rescheduleddatetime: Optional[datetime] = None
    rescheduledby: Optional[str] = None
    startcheckindatetime: Optional[datetime] = None
    stopsignoffdatetime: Optional[datetime] = None
    appointmentdeleteddatetime: Optional[datetime] = None
    claimid: Optional[int] = None
    cycletime: Optional[float] = None
    frozenyn: Optional[str] = None
    appointmentfrozenreason: Optional[str] = None
    webschedulableyn: Optional[int] = None

    # Computed fields stored for ML
    virtual_flag: VirtualFlag = VirtualFlag.NON_VIRTUAL
    new_patient_flag: NewPatientFlag = NewPatientFlag.ESTABLISHED

    @property
    def appointmentdatetime(self) -> datetime:
        """Combined appointment date and start time."""
        hour, minute = map(int, self.appointmentstarttime.split(":"))
        return datetime.combine(self.appointmentdate, datetime.min.time().replace(hour=hour, minute=minute))

    @property
    def lead_time_days(self) -> int:
        """Days between scheduling and appointment datetime."""
        if self.appointmentscheduleddatetime is None:
            return 0
        delta = self.appointmentdatetime - self.appointmentscheduleddatetime
        return max(0, delta.days)

    @property
    def day_of_week(self) -> int:
        """Day of week (Monday=0, Sunday=6)."""
        return self.appointmentdate.weekday()

    @property
    def hour_of_day(self) -> int:
        """Hour of appointment (0-23)."""
        return int(self.appointmentstarttime.split(":")[0])

    @property
    def is_past(self) -> bool:
        """Check if appointment is in the past."""
        appt_dt = self.appointmentdatetime
        if appt_dt.tzinfo is None:
            appt_dt = appt_dt.replace(tzinfo=timezone.utc)
        return appt_dt < datetime.now(timezone.utc)

    @property
    def no_show(self) -> bool:
        """Determine if appointment was a no-show."""
        if self.appointmentstatus == AppointmentStatus.NO_SHOW:
            return True
        # Past scheduled appointment with no check-in
        if (
            self.is_past
            and self.appointmentstatus == AppointmentStatus.SCHEDULED
            and self.appointmentcheckindatetime is None
        ):
            return True
        return False


@dataclass
class RiskFactor:
    """Represents a contributing factor to a prediction."""

    factor_name: str
    factor_value: str
    contribution: Decimal  # 0.0-1.0
    direction: RiskDirection


@dataclass
class Prediction:
    """Represents an ML model prediction for an appointment."""

    prediction_id: UUID
    appointmentid: int  # FK to Appointment
    no_show_probability: Decimal  # 0.0-1.0
    model_version: str
    risk_factors: list[RiskFactor] = field(default_factory=list)
    predicted_at: datetime = field(default_factory=datetime.utcnow)

    @property
    def risk_level(self) -> RiskLevel:
        """Categorize risk based on probability threshold."""
        prob = float(self.no_show_probability)
        if prob < 0.3:
            return RiskLevel.LOW
        elif prob <= 0.6:
            return RiskLevel.MEDIUM
        else:
            return RiskLevel.HIGH


@dataclass
class Recommendation:
    """Represents a suggested action based on prediction analysis."""

    recommendation_id: UUID
    appointment_id: int  # FK to Appointment
    action_type: ActionType
    priority: Priority
    rationale: str
    created_at: datetime = field(default_factory=datetime.utcnow)


# Type aliases for data generation
PatientList = list[Patient]
ProviderList = list[Provider]
DepartmentList = list[Department]
InsuranceList = list[Insurance]
AppointmentList = list[Appointment]
PredictionList = list[Prediction]
RecommendationList = list[Recommendation]
