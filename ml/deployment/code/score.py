"""Scoring script for no-show prediction model inference.

This script is used by Azure ML managed online endpoints to serve predictions.
It follows the Azure ML scoring script pattern with init() and run() functions.

Supports two input formats:
1. MLflow dataframe_split: {"input_data": {"columns": [...], "data": [[...]]}}
2. Custom appointments: {"appointments": [{...}]}

Returns probability scores from predict_proba().
"""

import json
import logging
import os

import joblib
import numpy as np
import pandas as pd

# Set up logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

# Global model reference
model = None

# Feature columns expected by the model (must match training data)
FEATURE_COLUMNS = [
    "patient_age_bucket", "patient_gender", "patient_zip_code", "patient_race_ethnicity",
    "portal_engaged", "historical_no_show_rate", "historical_no_show_count", "sipg2",
    "lead_time_days", "appointmenttypename", "virtual_flag", "new_patient_flag",
    "day_of_week", "hour_of_day", "appointmentduration", "webschedulableyn",
    "provider_specialty", "providertype", "departmentspecialty", "placeofservicetype", "market"
]


def init() -> None:
    """Initialize the model.

    This function is called once when the deployment starts.
    Load the model from the registered model directory.
    """
    global model

    logger.info("Initializing no-show prediction model...")

    # Get model path from environment
    model_dir = os.environ.get("AZUREML_MODEL_DIR", ".")
    logger.info(f"Model directory: {model_dir}")

    # List directory contents for debugging
    try:
        for root, dirs, files in os.walk(model_dir):
            for f in files:
                logger.info(f"Found: {os.path.join(root, f)}")
    except Exception as e:
        logger.warning(f"Could not list model directory: {e}")

    # Try MLflow model format first (most common for AutoML)
    # Check multiple possible paths for MLflow model
    mlflow_model_paths = [
        model_dir,
        os.path.join(model_dir, "mlflow-model"),
        os.path.join(model_dir, "1", "mlflow-model"),
    ]
    
    for mlflow_path in mlflow_model_paths:
        try:
            import mlflow
            logger.info(f"Trying to load MLflow model from {mlflow_path}")
            if os.path.exists(os.path.join(mlflow_path, "MLmodel")):
                model = mlflow.sklearn.load_model(mlflow_path)
                logger.info("Model loaded successfully via MLflow")
                
                # Log model type and capabilities
                logger.info(f"Model type: {type(model).__name__}")
                logger.info(f"Has predict_proba: {hasattr(model, 'predict_proba')}")
                return
        except Exception as e:
            logger.warning(f"MLflow load failed from {mlflow_path}: {e}")

    # Try direct pickle files
    model_paths = [
        os.path.join(model_dir, "model.pkl"),
        os.path.join(model_dir, "model", "model.pkl"),
        os.path.join(model_dir, "outputs", "model.pkl"),
        os.path.join(model_dir, "mlflow-model", "model.pkl"),
        os.path.join(model_dir, "1", "mlflow-model", "model.pkl"),
    ]

    for model_path in model_paths:
        if os.path.exists(model_path):
            logger.info(f"Loading model from {model_path}")
            model = joblib.load(model_path)
            logger.info(f"Model loaded successfully: {type(model).__name__}")
            return

    raise RuntimeError("Failed to load model from any known location")


def calculate_risk_level(probability: float) -> str:
    """Calculate risk level from probability.

    Thresholds from data-model.md:
    - Low: < 0.3
    - Medium: 0.3-0.6
    - High: > 0.6
    """
    if probability < 0.3:
        return "Low"
    elif probability <= 0.6:
        return "Medium"
    else:
        return "High"


def run(raw_data: str) -> str:
    """Run model inference on input data.

    Accepts two input formats:
    1. MLflow dataframe_split: {"input_data": {"columns": [...], "data": [[...]]}}
    2. Custom appointments: {"appointments": [{...}]}

    Returns JSON with predictions including probabilities.
    """
    global model

    if model is None:
        return json.dumps({"error": "Model not initialized"})

    try:
        data = json.loads(raw_data)
        logger.info(f"Received request with keys: {list(data.keys())}")

        # Parse input based on format
        if "input_data" in data:
            # MLflow dataframe_split format
            input_data = data["input_data"]
            columns = input_data.get("columns", FEATURE_COLUMNS)
            rows = input_data.get("data", [])
            df = pd.DataFrame(rows, columns=columns)
            logger.info(f"Parsed dataframe_split format: {len(df)} rows, columns: {list(df.columns)}")
        elif "appointments" in data:
            # Custom appointments format
            appointments = data["appointments"]
            df = pd.DataFrame(appointments)
            # Remove appointment_id if present
            if "appointment_id" in df.columns:
                df = df.drop(columns=["appointment_id"])
            logger.info(f"Parsed appointments format: {len(df)} rows")
        else:
            return json.dumps({"error": f"Unknown input format. Keys: {list(data.keys())}"})

        # Ensure all required columns are present
        missing_cols = set(FEATURE_COLUMNS) - set(df.columns)
        if missing_cols:
            logger.warning(f"Missing columns: {missing_cols}")
            # Add missing columns with defaults
            for col in missing_cols:
                df[col] = None

        # Reorder columns to match training order
        df = df[FEATURE_COLUMNS]

        # Get predictions with probabilities
        if hasattr(model, "predict_proba"):
            probabilities = model.predict_proba(df)
            # probabilities is shape (n_samples, n_classes)
            # For binary classification, column 1 is the no-show probability
            if probabilities.shape[1] > 1:
                no_show_probs = probabilities[:, 1].tolist()
            else:
                no_show_probs = probabilities[:, 0].tolist()
            logger.info(f"Using predict_proba, got {len(no_show_probs)} probabilities")
        else:
            # Fallback to binary predictions
            predictions = model.predict(df)
            no_show_probs = [1.0 if p == 1 else 0.0 for p in predictions]
            logger.warning("Model does not support predict_proba, using binary predictions")

        # Build response with probabilities
        result = {
            "predictions": [
                {
                    "no_show_probability": round(float(prob), 3),
                    "risk_level": calculate_risk_level(prob)
                }
                for prob in no_show_probs
            ]
        }

        logger.info(f"Returning {len(result['predictions'])} predictions")
        return json.dumps(result)

    except Exception as e:
        logger.error(f"Prediction error: {str(e)}", exc_info=True)
        return json.dumps({"error": str(e)})


# For local testing
if __name__ == "__main__":
    print("Testing scoring script locally...")
    
    # Create a mock model for testing
    from sklearn.ensemble import RandomForestClassifier
    model = RandomForestClassifier(n_estimators=10, random_state=42)
    
    # Train with dummy data matching expected features
    n_samples = 100
    X = pd.DataFrame({
        "patient_age_bucket": np.random.choice(["18-39", "40-64", "65+"], n_samples),
        "patient_gender": np.random.choice(["M", "F"], n_samples),
        "patient_zip_code": ["53715"] * n_samples,
        "patient_race_ethnicity": ["Unknown"] * n_samples,
        "portal_engaged": np.random.choice([True, False], n_samples),
        "historical_no_show_rate": np.random.uniform(0, 0.5, n_samples),
        "historical_no_show_count": np.random.randint(0, 5, n_samples),
        "sipg2": np.random.choice(["Commercial", "Medicare", "Medicaid"], n_samples),
        "lead_time_days": np.random.randint(1, 30, n_samples),
        "appointmenttypename": ["E&M EST PCP 3"] * n_samples,
        "virtual_flag": ["Non-Virtual"] * n_samples,
        "new_patient_flag": ["EST PATIENT"] * n_samples,
        "day_of_week": np.random.randint(0, 7, n_samples),
        "hour_of_day": np.random.randint(7, 18, n_samples),
        "appointmentduration": [30] * n_samples,
        "webschedulableyn": [1] * n_samples,
        "provider_specialty": ["Family Medicine"] * n_samples,
        "providertype": ["Physician"] * n_samples,
        "departmentspecialty": ["Family Medicine"] * n_samples,
        "placeofservicetype": ["Office"] * n_samples,
        "market": ["Madison"] * n_samples,
    })
    y = np.random.randint(0, 2, n_samples)
    
    # Convert categorical columns
    for col in X.select_dtypes(include=['object', 'bool']).columns:
        X[col] = X[col].astype('category').cat.codes
    
    model.fit(X, y)
    print(f"Mock model trained: {type(model).__name__}")
    print(f"Has predict_proba: {hasattr(model, 'predict_proba')}")
    
    # Test with dataframe_split format
    test_input = json.dumps({
        "input_data": {
            "columns": FEATURE_COLUMNS,
            "data": [
                ["40-64", "F", "53715", "Unknown", True, 0.15, 2, "Commercial", 7, "E&M EST PCP 3", 
                 "Non-Virtual", "EST PATIENT", 2, 10, 30, 1, "Family Medicine", "Physician", 
                 "Family Medicine", "Office", "Madison"],
                ["18-39", "M", "53705", "Unknown", False, 0.45, 8, "Medicaid", 21, "E&M NEW ADT",
                 "Non-Virtual", "NEW PATIENT", 0, 8, 45, 0, "Internal Medicine", "Physician",
                 "Internal Medicine", "Office", "Madison"]
            ]
        }
    })
    
    result = run(test_input)
    print("\nTest result:")
    print(json.dumps(json.loads(result), indent=2))
