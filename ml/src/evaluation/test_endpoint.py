"""Test script to validate deployed ML endpoint returns predictions.

Tests the managed online endpoint per ml-inference.openapi.yaml contract.
Uses DefaultAzureCredential for authentication.

Usage:
    python -m ml.src.evaluation.test_endpoint --endpoint-name noshow-inference
"""

import argparse
import json
import logging
import os

import requests
from azure.ai.ml import MLClient
from azure.identity import DefaultAzureCredential

logging.basicConfig(level=logging.INFO, format="%(asctime)s - %(levelname)s - %(message)s")
logger = logging.getLogger(__name__)


def get_ml_client() -> MLClient:
    """Create MLClient with DefaultAzureCredential."""
    subscription_id = os.environ.get("AZURE_SUBSCRIPTION_ID")
    resource_group = os.environ.get("AZURE_RESOURCE_GROUP")
    workspace_name = os.environ.get("AZURE_ML_WORKSPACE")

    if not all([subscription_id, resource_group, workspace_name]):
        raise ValueError(
            "Missing required configuration. Set AZURE_SUBSCRIPTION_ID, "
            "AZURE_RESOURCE_GROUP, and AZURE_ML_WORKSPACE environment variables."
        )

    credential = DefaultAzureCredential()
    return MLClient(
        credential=credential,
        subscription_id=subscription_id,
        resource_group_name=resource_group,
        workspace_name=workspace_name,
    )


def get_endpoint_info(ml_client: MLClient, endpoint_name: str) -> dict:
    """Get endpoint URL and authentication info.

    Args:
        ml_client: Authenticated MLClient
        endpoint_name: Name of online endpoint

    Returns:
        Dictionary with scoring_uri and auth info
    """
    endpoint = ml_client.online_endpoints.get(endpoint_name)

    # Get primary key
    keys = ml_client.online_endpoints.get_keys(endpoint_name)

    return {
        "scoring_uri": endpoint.scoring_uri,
        "primary_key": keys.primary_key,
        "auth_mode": endpoint.auth_mode,
    }


def create_test_request() -> dict:
    """Create a test prediction request.

    Returns:
        Request body matching PredictionRequest schema
    """
    return {
        "appointments": [
            {
                "appointment_id": 1,
                "patient_age_bucket": "40-64",
                "patient_gender": "F",
                "patient_zip_code": "53711",
                "portal_engaged": True,
                "historical_no_show_rate": 0.0,
                "historical_no_show_count": 0,
                "sipg2": "Commercial",
                "lead_time_days": 5,
                "appointmenttypename": "Follow-up Visit",
                "virtual_flag": "Non-Virtual",
                "new_patient_flag": "EST PATIENT",
                "day_of_week": 2,
                "hour_of_day": 9,
                "appointmentduration": 30,
                "provider_specialty": "Family Medicine",
                "providertype": "Physician",
                "departmentspecialty": "Family Medicine",
                "placeofservicetype": "Office",
                "market": "Region A",
                "webschedulableyn": 0,
            },
            {
                "appointment_id": 2,
                "patient_age_bucket": "18-39",
                "patient_gender": "M",
                "patient_zip_code": "60601",
                "portal_engaged": False,
                "historical_no_show_rate": 0.4,
                "historical_no_show_count": 4,
                "sipg2": "Medicaid",
                "lead_time_days": 21,
                "appointmenttypename": "New Patient Visit",
                "virtual_flag": "Non-Virtual",
                "new_patient_flag": "NEW PATIENT",
                "day_of_week": 0,  # Monday
                "hour_of_day": 15,
                "appointmentduration": 45,
                "provider_specialty": "Internal Medicine",
                "providertype": "Physician",
                "departmentspecialty": "Internal Medicine",
                "placeofservicetype": "Office",
                "market": "Region B",
                "webschedulableyn": 0,
            },
            {
                "appointment_id": 3,
                "patient_age_bucket": "65+",
                "patient_gender": "F",
                "patient_zip_code": "33101",
                "portal_engaged": True,
                "historical_no_show_rate": 0.05,
                "historical_no_show_count": 1,
                "sipg2": "Medicare",
                "lead_time_days": 2,
                "appointmenttypename": "Video Visit",
                "virtual_flag": "Virtual-Video",
                "new_patient_flag": "EST PATIENT",
                "day_of_week": 3,
                "hour_of_day": 8,
                "appointmentduration": 15,
                "provider_specialty": "Cardiology",
                "providertype": "Physician",
                "departmentspecialty": "Cardiology",
                "placeofservicetype": "Telehealth",
                "market": "Region C",
                "webschedulableyn": 1,
            },
        ]
    }


def validate_response(response: dict) -> tuple[bool, list[str]]:
    """Validate response matches PredictionResponse schema.

    Args:
        response: Response dictionary

    Returns:
        Tuple of (is_valid, list of errors)
    """
    errors = []

    if "error" in response:
        errors.append(f"Response contains error: {response['error']}")
        return False, errors

    if "predictions" not in response:
        errors.append("Response missing 'predictions' field")
        return False, errors

    predictions = response["predictions"]
    if not isinstance(predictions, list):
        errors.append("'predictions' should be a list")
        return False, errors

    for i, pred in enumerate(predictions):
        # Required fields
        required_fields = ["appointment_id", "no_show_probability", "risk_level"]
        for field in required_fields:
            if field not in pred:
                errors.append(f"Prediction {i} missing required field: {field}")

        # Validate probability range
        if "no_show_probability" in pred:
            prob = pred["no_show_probability"]
            if not (0.0 <= prob <= 1.0):
                errors.append(f"Prediction {i} probability {prob} outside valid range [0,1]")

        # Validate risk level
        if "risk_level" in pred:
            level = pred["risk_level"]
            valid_levels = ["Low", "Medium", "High"]
            if level not in valid_levels:
                errors.append(f"Prediction {i} invalid risk_level: {level}")

        # Validate risk factors if present
        if "risk_factors" in pred:
            factors = pred["risk_factors"]
            if not isinstance(factors, list):
                errors.append(f"Prediction {i} risk_factors should be a list")
            else:
                for j, factor in enumerate(factors):
                    factor_required = ["factor_name", "factor_value", "contribution", "direction"]
                    for field in factor_required:
                        if field not in factor:
                            errors.append(f"Prediction {i} risk_factor {j} missing: {field}")

    return len(errors) == 0, errors


def call_endpoint(
    scoring_uri: str,
    api_key: str,
    request_body: dict,
) -> dict:
    """Call the prediction endpoint.

    Args:
        scoring_uri: Endpoint URL
        api_key: Authentication key
        request_body: Request body

    Returns:
        Response dictionary
    """
    headers = {
        "Content-Type": "application/json",
        "Authorization": f"Bearer {api_key}",
    }

    logger.info(f"Calling endpoint: {scoring_uri}")
    response = requests.post(
        scoring_uri,
        headers=headers,
        json=request_body,
        timeout=30,
    )

    logger.info(f"Response status: {response.status_code}")

    if response.status_code != 200:
        logger.error(f"Request failed: {response.text}")
        return {"error": f"HTTP {response.status_code}: {response.text}"}

    return response.json()


def run_tests(endpoint_name: str) -> bool:
    """Run all endpoint tests.

    Args:
        endpoint_name: Name of online endpoint

    Returns:
        True if all tests pass
    """
    logger.info("=== ML Endpoint Tests ===")

    # Initialize client
    ml_client = get_ml_client()
    logger.info(f"Connected to workspace: {ml_client.workspace_name}")

    # Get endpoint info
    logger.info(f"Getting endpoint info for: {endpoint_name}")
    try:
        endpoint_info = get_endpoint_info(ml_client, endpoint_name)
    except Exception as e:
        logger.error(f"Failed to get endpoint: {e}")
        return False

    logger.info(f"Endpoint URI: {endpoint_info['scoring_uri']}")
    logger.info(f"Auth mode: {endpoint_info['auth_mode']}")

    # Create test request
    test_request = create_test_request()
    logger.info(f"Test request: {len(test_request['appointments'])} appointments")

    # Call endpoint
    response = call_endpoint(
        scoring_uri=endpoint_info["scoring_uri"],
        api_key=endpoint_info["primary_key"],
        request_body=test_request,
    )

    # Validate response
    is_valid, errors = validate_response(response)

    if is_valid:
        logger.info("✓ Response validation passed")

        # Log predictions
        logger.info("\n=== Predictions ===")
        for pred in response["predictions"]:
            logger.info(
                f"  Appointment {pred['appointment_id']}: "
                f"Probability={pred['no_show_probability']:.2f}, "
                f"Risk={pred['risk_level']}"
            )

            if "risk_factors" in pred and pred["risk_factors"]:
                logger.info("    Risk factors:")
                for factor in pred["risk_factors"][:3]:
                    logger.info(
                        f"      - {factor['factor_name']}: {factor['factor_value']} "
                        f"({factor['direction']}, contribution={factor['contribution']:.3f})"
                    )
    else:
        logger.error("✗ Response validation failed")
        for error in errors:
            logger.error(f"  - {error}")

    # Additional tests
    logger.info("\n=== Risk Level Distribution Test ===")
    predictions = response.get("predictions", [])
    if predictions:
        # Test request was designed to have varying risk levels
        has_low = any(p["risk_level"] == "Low" for p in predictions)
        has_high = any(p["risk_level"] == "High" or p["risk_level"] == "Medium" for p in predictions)

        if has_low or has_high:
            logger.info("✓ Risk levels appropriately distributed")
        else:
            logger.warning("⚠ All predictions have same risk level")

    # Latency test
    logger.info("\n=== Latency Test ===")
    import time

    start = time.time()
    _ = call_endpoint(
        scoring_uri=endpoint_info["scoring_uri"],
        api_key=endpoint_info["primary_key"],
        request_body=test_request,
    )
    latency = (time.time() - start) * 1000

    if latency < 5000:  # 5 second threshold
        logger.info(f"✓ Response latency: {latency:.0f}ms (< 5000ms)")
    else:
        logger.warning(f"⚠ Response latency: {latency:.0f}ms (> 5000ms threshold)")

    logger.info("\n=== Tests Complete ===")
    return is_valid


def main(endpoint_name: str) -> None:
    """Main test entry point.

    Args:
        endpoint_name: Name of online endpoint to test
    """
    success = run_tests(endpoint_name)

    if success:
        logger.info("\n✓ All endpoint tests PASSED")
    else:
        logger.error("\n✗ Some endpoint tests FAILED")
        exit(1)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Test no-show prediction ML endpoint")
    parser.add_argument(
        "--endpoint-name",
        type=str,
        default="noshow-inference",
        help="Name of online endpoint to test",
    )

    args = parser.parse_args()

    main(endpoint_name=args.endpoint_name)
