"""Prepare ML training data by joining tables and engineering features.

This script joins appointments with patients, providers, departments, and insurance,
then computes derived features required for no-show prediction.

Usage:
    python -m ml.src.data.prepare_ml_data [--output-dir ml/data/ml_prepared]
"""

import argparse
import logging
from pathlib import Path

import pandas as pd

logging.basicConfig(level=logging.INFO, format="%(asctime)s - %(levelname)s - %(message)s")
logger = logging.getLogger(__name__)


def load_synthetic_data(data_dir: Path) -> dict[str, pd.DataFrame]:
    """Load all synthetic data parquet files.
    
    Args:
        data_dir: Path to synthetic data directory
        
    Returns:
        Dictionary of table name -> DataFrame
    """
    tables = {}
    for name in ["appointments", "patients", "providers", "departments", "insurance"]:
        path = data_dir / f"{name}.parquet"
        if path.exists():
            tables[name] = pd.read_parquet(path)
            logger.info(f"Loaded {name}: {len(tables[name]):,} rows, {len(tables[name].columns)} columns")
        else:
            logger.warning(f"Missing {path}")
    return tables


def compute_historical_no_show_features(appointments: pd.DataFrame) -> pd.DataFrame:
    """Compute per-patient historical no-show statistics.
    
    Calculates features based on appointments BEFORE each appointment's date
    to avoid data leakage.
    
    Args:
        appointments: Appointments DataFrame with patientid and appointmentdate
        
    Returns:
        DataFrame with patientid, appointmentid, historical_no_show_count, historical_no_show_rate
    """
    logger.info("Computing historical no-show features per patient...")
    
    # Ensure date types
    appointments = appointments.copy()
    appointments["appointmentdate"] = pd.to_datetime(appointments["appointmentdate"])
    
    # Sort by patient and date
    appointments = appointments.sort_values(["patientid", "appointmentdate"])
    
    # Create no_show flag
    appointments["no_show"] = (appointments["appointmentstatus"] == "No Show").astype(int)
    
    # For each appointment, compute stats from prior appointments only
    results = []
    
    for patient_id, group in appointments.groupby("patientid"):
        group = group.sort_values("appointmentdate")
        
        # Cumulative count and sum (shifted to exclude current row)
        group["cumulative_appointments"] = range(len(group))
        group["cumulative_no_shows"] = group["no_show"].cumsum().shift(1, fill_value=0)
        
        # Historical rate (avoid division by zero)
        group["historical_no_show_count"] = group["cumulative_no_shows"]
        group["historical_no_show_rate"] = group.apply(
            lambda row: row["cumulative_no_shows"] / row["cumulative_appointments"] 
            if row["cumulative_appointments"] > 0 else 0.0,
            axis=1
        )
        
        results.append(group[["appointmentid", "patientid", "historical_no_show_count", "historical_no_show_rate"]])
    
    return pd.concat(results, ignore_index=True)


def extract_time_features(appointments: pd.DataFrame) -> pd.DataFrame:
    """Extract time-based features from appointment dates/times.
    
    Args:
        appointments: Appointments DataFrame
        
    Returns:
        DataFrame with appointmentid and time features
    """
    logger.info("Extracting time-based features...")
    
    df = appointments.copy()
    
    # Parse dates
    df["appointmentdate"] = pd.to_datetime(df["appointmentdate"])
    df["appointmentcreateddatetime"] = pd.to_datetime(df["appointmentcreateddatetime"])
    df["appointmentscheduleddatetime"] = pd.to_datetime(df["appointmentscheduleddatetime"])
    
    # Day of week (Monday=0, Sunday=6)
    df["day_of_week"] = df["appointmentdate"].dt.dayofweek
    
    # Hour of day from start time string (e.g., "09:30" -> 9)
    df["hour_of_day"] = df["appointmentstarttime"].str.split(":").str[0].astype(int)
    
    # Lead time: days between scheduling and appointment
    df["lead_time_days"] = (df["appointmentdate"] - df["appointmentscheduleddatetime"].dt.normalize()).dt.days
    df["lead_time_days"] = df["lead_time_days"].clip(lower=0)  # Can't be negative
    
    return df[["appointmentid", "day_of_week", "hour_of_day", "lead_time_days"]]


def compute_patient_age_bucket(patients: pd.DataFrame) -> pd.DataFrame:
    """Compute age bucket from birth date (if available) or assign randomly.
    
    Args:
        patients: Patients DataFrame
        
    Returns:
        DataFrame with patientid and patient_age_bucket
    """
    logger.info("Computing patient age buckets...")
    
    df = patients.copy()
    
    # If patient_age_bucket already exists, use it
    if "patient_age_bucket" in df.columns:
        return df[["patientid", "patient_age_bucket"]]
    
    # Otherwise, we'd compute from birth date - for now just return empty
    logger.warning("patient_age_bucket not found in patients data")
    return df[["patientid"]].assign(patient_age_bucket="Unknown")


def prepare_ml_dataset(
    data_dir: Path,
    output_dir: Path,
    test_size: float = 0.2,
    random_state: int = 42,
) -> None:
    """Prepare complete ML dataset with all features.
    
    Args:
        data_dir: Path to synthetic data directory
        output_dir: Path to output directory for ML data
        test_size: Fraction for test split
        random_state: Random seed for reproducibility
    """
    from sklearn.model_selection import train_test_split
    
    # Load all tables
    tables = load_synthetic_data(data_dir)
    
    if "appointments" not in tables:
        raise ValueError("appointments.parquet is required")
    
    appointments = tables["appointments"].copy()
    logger.info(f"Starting with {len(appointments):,} appointments")
    
    # Filter to past appointments only (can't train on future outcomes)
    appointments["appointmentdate"] = pd.to_datetime(appointments["appointmentdate"])
    today = pd.Timestamp.today().normalize()
    appointments = appointments[appointments["appointmentdate"] < today].copy()
    logger.info(f"After filtering to past: {len(appointments):,} appointments")
    
    # Create target column
    appointments["no_show"] = (appointments["appointmentstatus"] == "No Show").astype(int)
    logger.info(f"No-show rate: {appointments['no_show'].mean():.1%}")
    
    # =========================================================================
    # Join with related tables
    # =========================================================================
    
    # Join patients
    if "patients" in tables:
        patients = tables["patients"]
        # Select relevant patient columns
        patient_cols = ["patientid"]
        for col in ["patient_gender", "patient_age_bucket", "patient_race_ethnicity", 
                    "patient_zip_code", "portal_last_login"]:
            if col in patients.columns:
                patient_cols.append(col)
        
        appointments = appointments.merge(
            patients[patient_cols],
            on="patientid",
            how="left"
        )
        logger.info(f"Joined patients: {len(appointments):,} rows")
        
        # Compute portal_engaged (login within 90 days of appointment)
        if "portal_last_login" in appointments.columns:
            appointments["portal_last_login"] = pd.to_datetime(appointments["portal_last_login"], utc=True)
            appointments["appointmentdate_tz"] = appointments["appointmentdate"].dt.tz_localize("UTC")
            appointments["portal_engaged"] = (
                (appointments["appointmentdate_tz"] - appointments["portal_last_login"]).dt.days.abs() <= 90
            ).fillna(False).astype(int)
            appointments = appointments.drop(columns=["appointmentdate_tz"])
        else:
            appointments["portal_engaged"] = 0
    
    # Join insurance
    if "insurance" in tables:
        insurance = tables["insurance"]
        insurance_cols = ["patientid"]
        for col in ["sipg2", "sipg1"]:
            if col in insurance.columns:
                insurance_cols.append(col)
        
        appointments = appointments.merge(
            insurance[insurance_cols],
            on="patientid",
            how="left"
        )
        logger.info(f"Joined insurance: {len(appointments):,} rows")
    
    # Join providers
    if "providers" in tables:
        providers = tables["providers"]
        provider_cols = ["providerid"]
        for col in ["providertype", "provider_specialty"]:
            if col in providers.columns:
                provider_cols.append(col)
        
        appointments = appointments.merge(
            providers[provider_cols],
            on="providerid",
            how="left"
        )
        logger.info(f"Joined providers: {len(appointments):,} rows")
    
    # Join departments
    if "departments" in tables:
        departments = tables["departments"]
        dept_cols = ["departmentid"]
        for col in ["departmentspecialty", "placeofservicetype", "market"]:
            if col in departments.columns:
                dept_cols.append(col)
        
        appointments = appointments.merge(
            departments[dept_cols],
            on="departmentid",
            how="left"
        )
        logger.info(f"Joined departments: {len(appointments):,} rows")
    
    # =========================================================================
    # Compute derived features
    # =========================================================================
    
    # Time features
    time_features = extract_time_features(appointments)
    appointments = appointments.merge(time_features, on="appointmentid", how="left")
    
    # Historical no-show features (computed per patient from prior appointments)
    history_features = compute_historical_no_show_features(appointments)
    appointments = appointments.merge(
        history_features[["appointmentid", "historical_no_show_count", "historical_no_show_rate"]],
        on="appointmentid",
        how="left"
    )
    
    # =========================================================================
    # Select final feature set
    # =========================================================================
    
    # Define features to include (matching config.yaml)
    feature_columns = [
        # Patient features
        "patient_age_bucket",
        "patient_gender",
        "patient_zip_code",
        "patient_race_ethnicity",
        "portal_engaged",
        "historical_no_show_rate",
        "historical_no_show_count",
        # Insurance features
        "sipg2",
        # Appointment features
        "lead_time_days",
        "appointmenttypename",
        "virtual_flag",
        "new_patient_flag",
        "day_of_week",
        "hour_of_day",
        "appointmentduration",
        "webschedulableyn",
        # Provider features
        "provider_specialty",
        "providertype",
        # Department features
        "departmentspecialty",
        "placeofservicetype",
        "market",
    ]
    
    # Target column
    target_column = "no_show"
    
    # Keep only available features
    available_features = [f for f in feature_columns if f in appointments.columns]
    missing_features = set(feature_columns) - set(available_features)
    
    if missing_features:
        logger.warning(f"Missing features (will be excluded): {sorted(missing_features)}")
    
    logger.info(f"Available features: {len(available_features)}")
    
    # Build final dataset
    df_final = appointments[available_features + [target_column]].copy()
    
    # Drop rows with missing target
    df_final = df_final.dropna(subset=[target_column])
    
    # Fill missing values for features
    for col in df_final.columns:
        if df_final[col].dtype == "object":
            df_final[col] = df_final[col].fillna("Unknown")
        else:
            df_final[col] = df_final[col].fillna(0)
    
    logger.info(f"Final dataset: {len(df_final):,} rows, {len(df_final.columns)} columns")
    logger.info(f"Final no-show rate: {df_final[target_column].mean():.1%}")
    
    # =========================================================================
    # Train/test split
    # =========================================================================
    
    train_df, test_df = train_test_split(
        df_final,
        test_size=test_size,
        random_state=random_state,
        stratify=df_final[target_column],
    )
    
    logger.info(f"Training set: {len(train_df):,} rows")
    logger.info(f"Test set: {len(test_df):,} rows")
    
    # =========================================================================
    # Save outputs
    # =========================================================================
    
    output_dir.mkdir(parents=True, exist_ok=True)
    
    train_path = output_dir / "train.parquet"
    test_path = output_dir / "test.parquet"
    
    train_df.to_parquet(train_path, index=False)
    test_df.to_parquet(test_path, index=False)
    
    logger.info(f"Saved training data to {train_path}")
    logger.info(f"Saved test data to {test_path}")
    
    # Create MLTable file for Azure ML
    mltable_content = """$schema: https://azuremlschemas.azureedge.net/latest/MLTable.schema.json
type: mltable

paths:
  - file: ./train.parquet

transformations:
  - read_parquet:
      include_path_column: false
"""
    
    mltable_path = output_dir / "MLTable"
    mltable_path.write_text(mltable_content)
    logger.info(f"Created MLTable at {mltable_path}")
    
    # Print feature summary
    print("\n" + "=" * 60)
    print("FEATURE SUMMARY")
    print("=" * 60)
    print(f"\nFeatures included ({len(available_features)}):")
    for f in sorted(available_features):
        dtype = df_final[f].dtype
        nunique = df_final[f].nunique()
        print(f"  - {f}: {dtype} ({nunique} unique values)")
    
    print(f"\nTarget: {target_column}")
    print(f"  - Class 0 (Show): {(df_final[target_column] == 0).sum():,}")
    print(f"  - Class 1 (No-show): {(df_final[target_column] == 1).sum():,}")
    print("=" * 60)


def main():
    parser = argparse.ArgumentParser(description="Prepare ML training data")
    parser.add_argument(
        "--data-dir",
        type=Path,
        default=Path("ml/data/synthetic"),
        help="Path to synthetic data directory",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=Path("ml/data/ml_prepared"),
        help="Path to output directory",
    )
    parser.add_argument(
        "--test-size",
        type=float,
        default=0.2,
        help="Fraction for test split",
    )
    parser.add_argument(
        "--random-state",
        type=int,
        default=42,
        help="Random seed",
    )
    
    args = parser.parse_args()
    
    prepare_ml_dataset(
        data_dir=args.data_dir,
        output_dir=args.output_dir,
        test_size=args.test_size,
        random_state=args.random_state,
    )


if __name__ == "__main__":
    main()
