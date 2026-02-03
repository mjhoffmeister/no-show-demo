"""Deploy trained model to Azure ML managed endpoint.

This script handles:
1. Finding the best model from an AutoML job
2. Registering the model in Azure ML Model Registry
3. Creating/updating the managed online endpoint
4. Deploying the model to the endpoint

Usage:
    # Deploy best model from a specific AutoML job
    python -m ml.src.deployment.deploy_model --job-name calm_bulb_0gtr3bskvp

    # Deploy with custom model name and version
    python -m ml.src.deployment.deploy_model --job-name calm_bulb_0gtr3bskvp --model-name noshow-predictor --model-version 2

    # Deploy specific child run (if you know the best model)
    python -m ml.src.deployment.deploy_model --job-name calm_bulb_0gtr3bskvp --child-run-suffix 3

Environment Variables:
    AZURE_SUBSCRIPTION_ID: Azure subscription ID
    AZURE_RESOURCE_GROUP: Resource group name
    AZURE_ML_WORKSPACE: Azure ML workspace name
"""

import argparse
import os
import sys
import time
from azure.ai.ml import MLClient
from azure.ai.ml.entities import (
    Model,
    ManagedOnlineEndpoint,
    ManagedOnlineDeployment,
    CodeConfiguration,
    Environment,
    OnlineRequestSettings,
    ProbeSettings,
)
from azure.ai.ml.constants import AssetTypes
from azure.identity import DefaultAzureCredential


def get_ml_client() -> MLClient:
    """Create Azure ML client from environment variables."""
    subscription_id = os.environ.get("AZURE_SUBSCRIPTION_ID")
    resource_group = os.environ.get("AZURE_RESOURCE_GROUP")
    workspace_name = os.environ.get("AZURE_ML_WORKSPACE")

    if not all([subscription_id, resource_group, workspace_name]):
        raise ValueError(
            "Missing required environment variables. Set:\n"
            "  AZURE_SUBSCRIPTION_ID\n"
            "  AZURE_RESOURCE_GROUP\n"
            "  AZURE_ML_WORKSPACE"
        )

    return MLClient(
        credential=DefaultAzureCredential(),
        subscription_id=subscription_id,
        resource_group_name=resource_group,
        workspace_name=workspace_name,
    )


def find_best_child_run(ml_client: MLClient, parent_job_name: str) -> str:
    """Find the best child run from an AutoML job.
    
    Returns the child run name (e.g., 'calm_bulb_0gtr3bskvp_3').
    """
    import mlflow
    
    # Set MLflow tracking URI
    workspace = ml_client.workspaces.get(ml_client.workspace_name)
    mlflow.set_tracking_uri(workspace.mlflow_tracking_uri)
    
    # Get the parent job to find its experiment
    parent_job = ml_client.jobs.get(parent_job_name)
    experiment_name = parent_job.experiment_name
    
    # Search for child runs, ordered by primary metric
    # AutoML jobs use different metrics; try common ones
    runs = mlflow.search_runs(
        experiment_names=[experiment_name],
        filter_string=f"tags.mlflow.parentRunId = '{parent_job_name}'",
        max_results=50,
    )
    
    if runs.empty:
        raise ValueError(f"No child runs found for job {parent_job_name}")
    
    # Filter to actual model runs (exclude setup, featurize, etc.)
    model_runs = runs[runs["run_id"].str.contains("_\\d+$", regex=True, na=False) == False]
    
    # Try to find best by various metrics
    metric_columns = [col for col in runs.columns if col.startswith("metrics.")]
    
    # Prefer norm_macro_recall for balanced classification
    if "metrics.norm_macro_recall" in metric_columns:
        best_run = runs.loc[runs["metrics.norm_macro_recall"].idxmax()]
    elif "metrics.AUC_weighted" in metric_columns:
        best_run = runs.loc[runs["metrics.AUC_weighted"].idxmax()]
    elif "metrics.accuracy" in metric_columns:
        best_run = runs.loc[runs["metrics.accuracy"].idxmax()]
    else:
        # Fallback: take the first run that has model outputs
        best_run = runs.iloc[0]
    
    return best_run["run_id"]


def register_model(
    ml_client: MLClient,
    job_name: str,
    child_run_suffix: int | None,
    model_name: str,
    model_version: str,
) -> Model:
    """Register model from AutoML job output."""
    
    # Construct child run name
    if child_run_suffix is not None:
        child_run_name = f"{job_name}_{child_run_suffix}"
    else:
        # Find best child run automatically
        print("Finding best child run...")
        child_run_name = find_best_child_run(ml_client, job_name)
        print(f"Best child run: {child_run_name}")
    
    # Model path in job outputs
    model_path = f"azureml://jobs/{child_run_name}/outputs/artifacts/outputs/mlflow-model"
    
    print(f"Registering model from: {model_path}")
    
    model = Model(
        name=model_name,
        version=model_version,
        path=model_path,
        type=AssetTypes.MLFLOW_MODEL,
        description=f"No-show prediction model from AutoML job {job_name}",
        tags={
            "source_job": job_name,
            "child_run": child_run_name,
            "task": "classification",
            "target": "no_show",
        },
    )
    
    registered_model = ml_client.models.create_or_update(model)
    print(f"Model registered: {registered_model.name}:{registered_model.version}")
    
    return registered_model


def ensure_endpoint(ml_client: MLClient, endpoint_name: str) -> ManagedOnlineEndpoint:
    """Create endpoint if it doesn't exist."""
    try:
        endpoint = ml_client.online_endpoints.get(endpoint_name)
        print(f"Endpoint '{endpoint_name}' exists (state: {endpoint.provisioning_state})")
        return endpoint
    except Exception:
        print(f"Creating endpoint '{endpoint_name}'...")
        endpoint = ManagedOnlineEndpoint(
            name=endpoint_name,
            description="No-show prediction model endpoint",
            auth_mode="aad_token",
            tags={
                "project": "no-show-demo",
                "environment": "development",
            },
        )
        ml_client.online_endpoints.begin_create_or_update(endpoint).result()
        print(f"Endpoint '{endpoint_name}' created")
        return ml_client.online_endpoints.get(endpoint_name)


def wait_for_deployment(
    ml_client: MLClient,
    endpoint_name: str,
    deployment_name: str,
    poll_interval: int = 30,
    max_wait_minutes: int = 20,
) -> str:
    """Poll deployment status until complete or failed."""
    import time
    
    max_iterations = (max_wait_minutes * 60) // poll_interval
    for i in range(max_iterations):
        try:
            deployment = ml_client.online_deployments.get(deployment_name, endpoint_name)
            state = deployment.provisioning_state
            print(f"  [{i+1}/{max_iterations}] Deployment state: {state}")
            
            if state == "Succeeded":
                return state
            elif state in ("Failed", "Canceled"):
                raise RuntimeError(f"Deployment {state}: check Azure portal for details")
            
            time.sleep(poll_interval)
        except Exception as e:
            if "not found" in str(e).lower():
                print(f"  Deployment not found yet, waiting...")
                time.sleep(poll_interval)
            else:
                raise
    
    raise TimeoutError(f"Deployment did not complete within {max_wait_minutes} minutes")


def deploy_model(
    ml_client: MLClient,
    endpoint_name: str,
    deployment_name: str,
    model_name: str,
    model_version: str,
) -> ManagedOnlineDeployment:
    """Deploy model to endpoint using no-code MLflow deployment."""
    
    # Check if deployment already exists and is in progress
    try:
        existing = ml_client.online_deployments.get(deployment_name, endpoint_name)
        state = existing.provisioning_state
        print(f"Deployment '{deployment_name}' exists (state: {state})")
        
        if state == "Succeeded":
            print("Deployment already succeeded, skipping to traffic routing...")
        elif state in ("Creating", "Updating"):
            print("Deployment in progress, waiting for completion...")
            wait_for_deployment(ml_client, endpoint_name, deployment_name)
        elif state in ("Failed", "Canceled"):
            print(f"Previous deployment {state}, will recreate...")
            # Fall through to create new deployment
            raise Exception("recreate")
    except Exception as e:
        if "not found" not in str(e).lower() and "recreate" not in str(e):
            raise
        
        # Create new deployment
        print(f"Deploying {model_name}:{model_version} to {endpoint_name}/{deployment_name}...")
        
        deployment = ManagedOnlineDeployment(
            name=deployment_name,
            endpoint_name=endpoint_name,
            model=f"azureml:{model_name}:{model_version}",
            instance_type="Standard_DS3_v2",
            instance_count=1,
            request_settings=OnlineRequestSettings(
                request_timeout_ms=30000,
                max_concurrent_requests_per_instance=10,
            ),
            liveness_probe=ProbeSettings(
                initial_delay=30,
                period=10,
                timeout=2,
                failure_threshold=30,
            ),
            tags={
                "model_version": model_version,
                "project": "no-show-demo",
            },
        )
        
        print("Starting deployment (this may take 5-10 minutes)...")
        # Start deployment without blocking
        ml_client.online_deployments.begin_create_or_update(deployment)
        
        # Poll for completion
        wait_for_deployment(ml_client, endpoint_name, deployment_name)
    
    # Set 100% traffic to this deployment
    print("Routing 100% traffic to deployment...")
    endpoint = ml_client.online_endpoints.get(endpoint_name)
    endpoint.traffic = {deployment_name: 100}
    ml_client.online_endpoints.begin_create_or_update(endpoint).result()
    
    print(f"Deployment complete: {endpoint_name}/{deployment_name}")
    
    # Get endpoint scoring URI
    endpoint = ml_client.online_endpoints.get(endpoint_name)
    print(f"Scoring URI: {endpoint.scoring_uri}")
    
    return ml_client.online_deployments.get(deployment_name, endpoint_name)


def main():
    parser = argparse.ArgumentParser(description="Deploy model to Azure ML endpoint")
    parser.add_argument(
        "--job-name",
        required=True,
        help="AutoML parent job name (e.g., calm_bulb_0gtr3bskvp)",
    )
    parser.add_argument(
        "--child-run-suffix",
        type=int,
        default=None,
        help="Child run suffix if known (e.g., 3 for calm_bulb_0gtr3bskvp_3). If not provided, best run is auto-detected.",
    )
    parser.add_argument(
        "--model-name",
        default="noshow-predictor",
        help="Model name in registry (default: noshow-predictor)",
    )
    parser.add_argument(
        "--model-version",
        default="1",
        help="Model version (default: 1)",
    )
    parser.add_argument(
        "--endpoint-name",
        default="noshow-predictor",
        help="Endpoint name (default: noshow-predictor)",
    )
    parser.add_argument(
        "--deployment-name",
        default="noshow-model-v1",
        help="Deployment name (default: noshow-model-v1)",
    )
    parser.add_argument(
        "--skip-registration",
        action="store_true",
        help="Skip model registration (use existing registered model)",
    )
    
    args = parser.parse_args()
    
    print("=" * 60)
    print("No-Show Prediction Model Deployment")
    print("=" * 60)
    
    ml_client = get_ml_client()
    print(f"Workspace: {ml_client.workspace_name}")
    
    # Step 1: Register model
    if not args.skip_registration:
        register_model(
            ml_client,
            args.job_name,
            args.child_run_suffix,
            args.model_name,
            args.model_version,
        )
    else:
        print(f"Skipping registration, using existing model {args.model_name}:{args.model_version}")
    
    # Step 2: Ensure endpoint exists
    ensure_endpoint(ml_client, args.endpoint_name)
    
    # Step 3: Deploy model
    deploy_model(
        ml_client,
        args.endpoint_name,
        args.deployment_name,
        args.model_name,
        args.model_version,
    )
    
    print("=" * 60)
    print("Deployment complete!")
    print("=" * 60)


if __name__ == "__main__":
    main()
