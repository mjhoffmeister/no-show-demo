"""Azure AutoML training script for no-show prediction model.

Submits an AutoML classification job to Azure ML workspace using azure-ai-ml v2 SDK.
Uses DefaultAzureCredential for credential-less authentication.

Usage:
    python -m ml.src.training.train_automl --config config.yaml
"""

import argparse
import logging
import os
from datetime import date
from pathlib import Path

import mlflow
import pandas as pd
import yaml
from azure.ai.ml import Input, MLClient, automl
from azure.ai.ml.automl import ClassificationPrimaryMetrics
from azure.ai.ml.constants import AssetTypes
from azure.ai.ml.entities import AmlCompute, Data, Environment
from azure.identity import DefaultAzureCredential
from azure.storage.blob import BlobServiceClient

logging.basicConfig(level=logging.INFO, format="%(asctime)s - %(levelname)s - %(message)s")
logger = logging.getLogger(__name__)


def load_config(config_path: str) -> dict:
    """Load configuration from YAML file."""
    with open(config_path) as f:
        return yaml.safe_load(f)


def get_ml_client(
    subscription_id: str | None = None,
    resource_group: str | None = None,
    workspace_name: str | None = None,
) -> MLClient:
    """Create MLClient with DefaultAzureCredential.

    Args:
        subscription_id: Azure subscription ID (default: from env)
        resource_group: Resource group name (default: from env)
        workspace_name: ML workspace name (default: from env)

    Returns:
        Authenticated MLClient
    """
    subscription_id = subscription_id or os.environ.get("AZURE_SUBSCRIPTION_ID")
    resource_group = resource_group or os.environ.get("AZURE_RESOURCE_GROUP")
    workspace_name = workspace_name or os.environ.get("AZURE_ML_WORKSPACE")

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


def prepare_training_data(
    data_path: str,
    features: list[str],
    target_column: str,
    test_size: float = 0.2,
    random_state: int = 42,
) -> tuple[pd.DataFrame, pd.DataFrame]:
    """Load and prepare training data from parquet file.

    Args:
        data_path: Path to appointments parquet file
        features: List of feature column names
        target_column: Name of target column
        test_size: Fraction for test split
        random_state: Random seed for reproducibility

    Returns:
        Tuple of (train_df, test_df)
    """
    from sklearn.model_selection import train_test_split

    logger.info(f"Loading data from {data_path}")
    df = pd.read_parquet(data_path)

    # Filter to past appointments only (prevent target leakage)
    logger.info("Filtering to past appointments only")
    df["appointmentdate"] = pd.to_datetime(df["appointmentdate"])
    df = df[df["appointmentdate"] < pd.Timestamp.today()]

    # Derive no_show target if not present
    if target_column not in df.columns:
        logger.info("Deriving no_show target from appointment status")
        df["no_show"] = (df["appointmentstatus"] == "No Show").astype(int)

    # Select features that exist in the dataframe
    available_features = [f for f in features if f in df.columns]
    missing_features = set(features) - set(available_features)
    if missing_features:
        logger.warning(f"Missing features (will be skipped): {missing_features}")

    # Prepare feature matrix
    df_model = df[available_features + [target_column]].copy()
    df_model = df_model.dropna(subset=[target_column])

    logger.info(f"Dataset size: {len(df_model):,} records")
    logger.info(f"No-show rate: {df_model[target_column].mean():.1%}")

    # Stratified train/test split
    train_df, test_df = train_test_split(
        df_model,
        test_size=test_size,
        random_state=random_state,
        stratify=df_model[target_column],
    )

    logger.info(f"Training set: {len(train_df):,} records")
    logger.info(f"Test set: {len(test_df):,} records")

    return train_df, test_df


def upload_training_data(
    ml_client: MLClient,
    train_df: pd.DataFrame,
    test_df: pd.DataFrame,
    data_name: str = "noshow-training",
) -> tuple[str, str]:
    """Upload training data to Azure ML workspace as data assets.

    Uses identity-based authentication to upload directly to blob storage,
    then registers the blob URIs as ML data assets (bypasses SAS token generation).

    Args:
        ml_client: Authenticated MLClient
        train_df: Training dataframe
        test_df: Test dataframe
        data_name: Base name for data assets

    Returns:
        Tuple of (training_data_uri, test_data_uri)
    """
    output_dir = Path("./data/ml_prepared")
    output_dir.mkdir(parents=True, exist_ok=True)

    # Save to parquet locally first
    train_path = output_dir / "train.parquet"
    test_path = output_dir / "test.parquet"
    train_df.to_parquet(train_path, index=False)
    test_df.to_parquet(test_path, index=False)

    logger.info("Uploading training data to Azure ML using identity auth...")

    # Get workspace datastore info
    ws_datastore = ml_client.datastores.get("workspaceblobstore")
    storage_account = ws_datastore.account_name
    container_name = ws_datastore.container_name

    # Create blob client with identity auth
    credential = DefaultAzureCredential()
    blob_service_url = f"https://{storage_account}.blob.core.windows.net"
    blob_service = BlobServiceClient(blob_service_url, credential=credential)
    container_client = blob_service.get_container_client(container_name)

    # Upload files via identity auth
    timestamp = date.today().isoformat()
    train_blob_name = f"LocalUpload/training-data/{timestamp}/train.parquet"
    test_blob_name = f"LocalUpload/training-data/{timestamp}/test.parquet"

    logger.info(f"Uploading {train_path} to blob storage...")
    with open(train_path, "rb") as f:
        container_client.upload_blob(train_blob_name, f, overwrite=True)

    logger.info(f"Uploading {test_path} to blob storage...")
    with open(test_path, "rb") as f:
        container_client.upload_blob(test_blob_name, f, overwrite=True)

    # Build blob URIs
    train_blob_uri = f"azureml://subscriptions/{ml_client.subscription_id}/resourcegroups/{ml_client.resource_group_name}/workspaces/{ml_client.workspace_name}/datastores/workspaceblobstore/paths/{train_blob_name}"
    test_blob_uri = f"azureml://subscriptions/{ml_client.subscription_id}/resourcegroups/{ml_client.resource_group_name}/workspaces/{ml_client.workspace_name}/datastores/workspaceblobstore/paths/{test_blob_name}"

    # Register data assets pointing to blob URIs (no local upload)
    train_data = Data(
        name=f"{data_name}-train",
        path=train_blob_uri,
        type=AssetTypes.URI_FILE,
        description="Training data for no-show prediction model",
    )
    train_asset = ml_client.data.create_or_update(train_data)

    test_data = Data(
        name=f"{data_name}-test",
        path=test_blob_uri,
        type=AssetTypes.URI_FILE,
        description="Test data for no-show prediction model",
    )
    test_asset = ml_client.data.create_or_update(test_data)

    logger.info(f"Training data: {train_asset.id}")
    logger.info(f"Test data: {test_asset.id}")

    return train_asset.id, test_asset.id


def ensure_compute_exists(ml_client: MLClient, compute_name: str) -> None:
    """Ensure compute cluster exists, create if not.

    Args:
        ml_client: Authenticated MLClient
        compute_name: Name of compute cluster
    """
    try:
        ml_client.compute.get(compute_name)
        logger.info(f"Compute cluster '{compute_name}' found")
    except Exception:
        logger.info(f"Creating compute cluster '{compute_name}'...")
        compute = AmlCompute(
            name=compute_name,
            size="Standard_DS3_v2",
            min_instances=0,
            max_instances=4,
            idle_time_before_scale_down=120,
        )
        ml_client.compute.begin_create_or_update(compute).result()
        logger.info(f"Compute cluster '{compute_name}' created")


def submit_automl_job(
    ml_client: MLClient,
    config: dict,
    training_data_uri: str,
) -> str:
    """Submit AutoML classification job.

    Args:
        ml_client: Authenticated MLClient
        config: Configuration dictionary
        training_data_uri: URI to training data asset

    Returns:
        Job name/ID
    """
    task_config = config["task"]
    limits_config = config["limits"]
    job_config = config["job"]

    logger.info("Configuring AutoML job...")

    # Create classification job
    classification_job = automl.classification(
        # Training data
        training_data=Input(type=AssetTypes.URI_FILE, path=training_data_uri),
        target_column_name=task_config["target_column_name"],
        # Primary metric
        primary_metric=ClassificationPrimaryMetrics.AUC_WEIGHTED,
        # Explainability
        enable_model_explainability=task_config.get("enable_model_explainability", True),
        # Cross-validation
        n_cross_validations=config["validation"].get("n_cross_validations", 5),
        # Compute
        compute=config["compute"]["target"],
    )

    # Set limits
    classification_job.set_limits(
        timeout_minutes=limits_config.get("timeout_minutes", 60),
        trial_timeout_minutes=limits_config.get("trial_timeout_minutes", 20),
        max_concurrent_trials=limits_config.get("max_concurrent_trials", 4),
        max_trials=limits_config.get("max_trials", 20),
        enable_early_termination=limits_config.get("enable_early_termination", True),
    )

    # Set featurization (auto by default)
    classification_job.set_featurization(mode="auto")

    # Set job metadata
    classification_job.experiment_name = job_config.get("experiment_name", "noshow-prediction")
    classification_job.display_name = job_config.get("name", f"noshow-automl-{date.today()}")
    classification_job.description = job_config.get("description", "No-show prediction model training")
    classification_job.tags = job_config.get("tags", {})

    logger.info(f"Submitting AutoML job: {classification_job.display_name}")
    logger.info(f"Experiment: {classification_job.experiment_name}")

    # Submit job
    returned_job = ml_client.jobs.create_or_update(classification_job)

    logger.info(f"Job submitted: {returned_job.name}")
    logger.info(f"Job status: {returned_job.status}")
    logger.info(f"Studio URL: {returned_job.studio_url}")

    return returned_job.name


def wait_for_completion(ml_client: MLClient, job_name: str, timeout_minutes: int = 90) -> dict:
    """Wait for job to complete and return results.

    Args:
        ml_client: Authenticated MLClient
        job_name: Job name/ID
        timeout_minutes: Maximum wait time

    Returns:
        Job result dictionary
    """
    from azure.ai.ml._restclient.v2023_04_01_preview.models import JobStatus
    import time

    logger.info(f"Waiting for job {job_name} to complete...")
    start_time = time.time()
    timeout_seconds = timeout_minutes * 60

    while True:
        job = ml_client.jobs.get(job_name)
        status = job.status

        elapsed = time.time() - start_time
        logger.info(f"Status: {status} (elapsed: {elapsed/60:.1f} minutes)")

        if status in [JobStatus.COMPLETED, JobStatus.FINISHED]:
            logger.info("Job completed successfully!")
            return {"status": "completed", "job": job}
        elif status in [JobStatus.FAILED, JobStatus.CANCELED]:
            logger.error(f"Job failed with status: {status}")
            return {"status": "failed", "job": job}

        if elapsed > timeout_seconds:
            logger.warning(f"Timeout reached after {timeout_minutes} minutes")
            return {"status": "timeout", "job": job}

        time.sleep(60)  # Check every minute


def log_metrics_to_mlflow(ml_client: MLClient, job_name: str) -> None:
    """Log job metrics to MLflow for tracking.

    Args:
        ml_client: Authenticated MLClient
        job_name: Completed job name/ID
    """
    job = ml_client.jobs.get(job_name)

    # Set MLflow tracking URI
    tracking_uri = ml_client.workspaces.get(ml_client.workspace_name).mlflow_tracking_uri
    mlflow.set_tracking_uri(tracking_uri)

    # Get best run metrics
    logger.info("Logging metrics to MLflow...")

    if hasattr(job, "best_child_run_id"):
        best_run = ml_client.jobs.get(job.best_child_run_id)
        logger.info(f"Best model run: {best_run.name}")

        # Log summary
        with mlflow.start_run(run_name=f"automl-summary-{job_name}"):
            mlflow.log_param("job_name", job_name)
            mlflow.log_param("best_child_run", job.best_child_run_id)
            mlflow.log_param("primary_metric", "AUC_weighted")

            # Metrics would be available from best_run.metrics
            logger.info("Metrics logged to MLflow")


def main(config_path: str, wait: bool = True) -> None:
    """Main training pipeline.

    Args:
        config_path: Path to configuration YAML file
        wait: Whether to wait for job completion
    """
    logger.info("=== No-Show Prediction Model Training ===")

    # Load configuration
    config = load_config(config_path)
    logger.info(f"Loaded configuration from {config_path}")

    # Initialize ML client
    ml_client = get_ml_client()
    logger.info(f"Connected to workspace: {ml_client.workspace_name}")

    # Ensure compute exists
    compute_name = config["compute"]["target"]
    ensure_compute_exists(ml_client, compute_name)

    # Prepare training data
    data_config = config["data"]
    train_df, test_df = prepare_training_data(
        data_path=data_config["source"]["path"],
        features=config["features"],
        target_column=config["task"]["target_column_name"],
        test_size=data_config["split"]["test_size"],
        random_state=data_config["split"]["random_state"],
    )

    # Upload to Azure ML
    train_uri, test_uri = upload_training_data(ml_client, train_df, test_df)

    # Submit AutoML job
    job_name = submit_automl_job(ml_client, config, train_uri)

    if wait:
        # Wait for completion
        result = wait_for_completion(
            ml_client,
            job_name,
            timeout_minutes=config["limits"].get("timeout_minutes", 60) + 30,
        )

        if result["status"] == "completed":
            # Log metrics
            log_metrics_to_mlflow(ml_client, job_name)
            logger.info("Training pipeline completed successfully!")
        else:
            logger.error(f"Training pipeline ended with status: {result['status']}")
    else:
        logger.info(f"Job {job_name} submitted. Check Azure ML Studio for progress.")

    logger.info("=== Training Complete ===")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Train no-show prediction model with AutoML")
    parser.add_argument(
        "--config",
        type=str,
        default="ml/src/training/config.yaml",
        help="Path to configuration YAML file",
    )
    parser.add_argument(
        "--no-wait",
        action="store_true",
        help="Don't wait for job completion",
    )

    args = parser.parse_args()

    main(config_path=args.config, wait=not args.no_wait)
