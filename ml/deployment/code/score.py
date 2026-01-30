"""Scoring script for no-show prediction model inference.

This script is used by Azure ML managed online endpoints to serve predictions.
It follows the Azure ML scoring script pattern with init() and run() functions.

API contract matches ml-inference.openapi.yaml:
- Input: PredictionRequest with appointment_ids
- Output: PredictionResponse with predictions array
"""

import json
import logging
import os

import joblib
import numpy as np
import pandas as pd
from azureml.core.model import Model
from inference_schema.parameter_types.standard_py_parameter_type import StandardPythonParameterType
from inference_schema.schema_decorators import input_schema, output_schema

# Set up logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

# Global model reference
model = None


def init() -> None:
    """Initialize the model.

    This function is called once when the deployment starts.
    Load the model from the registered model directory.
    """
    global model

    logger.info("Initializing no-show prediction model...")

    # Get model path from environment
    model_dir = os.environ.get("AZUREML_MODEL_DIR", ".")

    # Try different model file patterns
    model_paths = [
        os.path.join(model_dir, "model.pkl"),
        os.path.join(model_dir, "model", "model.pkl"),
        os.path.join(model_dir, "outputs", "model.pkl"),
    ]

    for model_path in model_paths:
        if os.path.exists(model_path):
            logger.info(f"Loading model from {model_path}")
            model = joblib.load(model_path)
            break
    else:
        # Try MLflow model format
        try:
            import mlflow

            model_uri = model_dir
            logger.info(f"Loading MLflow model from {model_uri}")
            model = mlflow.sklearn.load_model(model_uri)
        except Exception as e:
            logger.error(f"Failed to load model: {e}")
            raise

    logger.info("Model loaded successfully")


# Schema for input validation and Swagger documentation
sample_input = {
    "appointments": [
        {
            "appointment_id": 12345,
            "patient_age_bucket": "40-64",
            "patient_gender": "F",
            "patient_zip_code": "53711",
            "portal_engaged": True,
            "historical_no_show_rate": 0.15,
            "historical_no_show_count": 2,
            "sipg2": "Commercial",
            "lead_time_days": 7,
            "appointmenttypename": "Follow-up Visit",
            "virtual_flag": "Non-Virtual",
            "new_patient_flag": "EST PATIENT",
            "day_of_week": 1,
            "hour_of_day": 10,
            "appointmentduration": 30,
            "provider_specialty": "Family Medicine",
            "providertype": "Physician",
            "departmentspecialty": "Family Medicine",
            "placeofservicetype": "Office",
            "market": "Region A",
            "webschedulableyn": 0,
        }
    ]
}

sample_output = {
    "predictions": [
        {
            "appointment_id": 12345,
            "no_show_probability": 0.35,
            "risk_level": "Medium",
            "risk_factors": [
                {
                    "factor_name": "historical_no_show_rate",
                    "factor_value": "0.15",
                    "contribution": 0.25,
                    "direction": "Increases",
                }
            ],
        }
    ]
}


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


def get_risk_factors(
    model,
    input_row: dict,
    probability: float,
) -> list[dict]:
    """Extract risk factors contributing to the prediction.

    Uses feature importance or SHAP values when available.

    Args:
        model: Trained model
        input_row: Input feature dictionary
        probability: Predicted probability

    Returns:
        List of risk factor dictionaries
    """
    risk_factors = []

    # Get feature importance if available
    if hasattr(model, "feature_importances_"):
        feature_names = list(input_row.keys())
        importances = model.feature_importances_

        # Get top 5 features
        importance_pairs = list(zip(feature_names, importances))
        importance_pairs.sort(key=lambda x: x[1], reverse=True)

        for feature, importance in importance_pairs[:5]:
            if importance > 0.01:  # Only include meaningful contributions
                direction = "Increases" if probability > 0.5 else "Decreases"
                risk_factors.append({
                    "factor_name": feature,
                    "factor_value": str(input_row.get(feature, "N/A")),
                    "contribution": round(float(importance), 3),
                    "direction": direction,
                })

    # Fallback: Use known high-risk factors from domain knowledge
    if not risk_factors:
        known_factors = [
            ("historical_no_show_rate", 0.15, "Increases"),
            ("lead_time_days", 0.10, "Increases"),
            ("sipg2", 0.08, "Varies"),
            ("day_of_week", 0.05, "Increases"),
            ("portal_engaged", 0.04, "Decreases"),
        ]

        for factor_name, weight, direction in known_factors:
            if factor_name in input_row:
                risk_factors.append({
                    "factor_name": factor_name,
                    "factor_value": str(input_row[factor_name]),
                    "contribution": weight,
                    "direction": direction,
                })

    return risk_factors[:5]  # Limit to top 5


@input_schema("data", StandardPythonParameterType(sample_input))
@output_schema(StandardPythonParameterType(sample_output))
def run(data: dict) -> str:
    """Run model inference on input data.

    Args:
        data: Dictionary with 'appointments' list containing feature dictionaries

    Returns:
        JSON string with predictions
    """
    global model

    logger.info(f"Received prediction request for {len(data.get('appointments', []))} appointments")

    if model is None:
        return json.dumps({"error": "Model not initialized"})

    try:
        appointments = data.get("appointments", [])
        predictions = []

        for appt in appointments:
            appointment_id = appt.get("appointment_id")

            # Build feature vector (excluding appointment_id)
            features = {k: v for k, v in appt.items() if k != "appointment_id"}

            # Convert to DataFrame for model
            df = pd.DataFrame([features])

            # Make prediction
            if hasattr(model, "predict_proba"):
                proba = model.predict_proba(df)[0]
                no_show_prob = float(proba[1]) if len(proba) > 1 else float(proba[0])
            else:
                # Binary prediction only
                pred = model.predict(df)[0]
                no_show_prob = 1.0 if pred == 1 else 0.0

            # Calculate risk level
            risk_level = calculate_risk_level(no_show_prob)

            # Get risk factors
            risk_factors = get_risk_factors(model, features, no_show_prob)

            predictions.append({
                "appointment_id": appointment_id,
                "no_show_probability": round(no_show_prob, 3),
                "risk_level": risk_level,
                "risk_factors": risk_factors,
            })

        result = {"predictions": predictions}
        logger.info(f"Returning {len(predictions)} predictions")
        return json.dumps(result)

    except Exception as e:
        logger.error(f"Prediction error: {str(e)}")
        return json.dumps({"error": str(e)})


# For local testing
if __name__ == "__main__":
    # Initialize model (requires model file in current directory)
    try:
        init()
    except Exception as e:
        print(f"Model not available for local testing: {e}")
        print("Creating mock model for testing...")
        from sklearn.ensemble import RandomForestClassifier

        model = RandomForestClassifier(n_estimators=10, random_state=42)
        # Fit with dummy data
        X = np.random.rand(100, 20)
        y = np.random.randint(0, 2, 100)
        model.fit(X, y)

    # Test prediction
    test_input = sample_input
    result = run(test_input)
    print("Test result:")
    print(json.dumps(json.loads(result), indent=2))
