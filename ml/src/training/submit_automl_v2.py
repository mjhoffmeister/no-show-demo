"""Submit AutoML job using registered MLTable data asset.

Usage:
    export AZURE_SUBSCRIPTION_ID=<subscription-id>
    export AZURE_RESOURCE_GROUP=<resource-group>
    export AZURE_ML_WORKSPACE=<workspace-name>
    python -m ml.src.training.submit_automl_v2
"""

import os
from azure.ai.ml import MLClient, Input, automl
from azure.ai.ml.constants import AssetTypes
from azure.ai.ml.automl import ClassificationPrimaryMetrics
from azure.identity import DefaultAzureCredential


def main():
    # Configuration from environment (no hardcoded defaults for portability)
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

    print(f"Connecting to workspace: {workspace_name}")

    # Connect to workspace
    credential = DefaultAzureCredential()
    ml_client = MLClient(
        credential=credential,
        subscription_id=subscription_id,
        resource_group_name=resource_group,
        workspace_name=workspace_name,
    )

    # Create the AutoML classification job with MLTable input
    # Version 3: Recalibrated synthetic data with stronger feature correlations
    # matching real-world Kaggle patterns for lead_time, age, and history effects
    training_data = Input(
        type=AssetTypes.MLTABLE,
        path="azureml:noshow-training-data:3"
    )
    
    # Holdout test data for final evaluation (not used during training/CV)
    test_data = Input(
        type=AssetTypes.MLTABLE,
        path="azureml:noshow-test-data:2"
    )
    
    print("Creating AutoML classification job...")
    
    # Use NORM_MACRO_RECALL to address class imbalance
    # This metric penalizes models that predict only the majority class
    # Formula: (recall_class0 + recall_class1) / 2, normalized to [0,1]
    classification_job = automl.classification(
        training_data=training_data,
        test_data=test_data,  # Evaluate best model on holdout test set
        target_column_name="no_show",
        primary_metric=ClassificationPrimaryMetrics.NORM_MACRO_RECALL,
        positive_label=True,  # No-show=True is the class we care about detecting
        enable_model_explainability=True,
        n_cross_validations=5,
        compute="cpu-cluster",
    )
    
    # Set limits
    classification_job.set_limits(
        timeout_minutes=60,
        trial_timeout_minutes=20,
        max_concurrent_trials=2,  # Match cluster max nodes
        max_trials=20,
        enable_early_termination=True,
    )
    
    # Set featurization
    classification_job.set_featurization(mode="auto")
    
    # Set job metadata
    classification_job.experiment_name = "noshow-prediction"
    classification_job.display_name = "No-Show Prediction AutoML v3 - Balanced"
    classification_job.description = "AutoML with NORM_MACRO_RECALL to address class imbalance"
    
    print("Submitting job...")
    returned_job = ml_client.jobs.create_or_update(classification_job)
    
    print(f"Job submitted: {returned_job.name}")
    print(f"Status: {returned_job.status}")
    print(f"Studio URL: {returned_job.studio_url}")
    
    return returned_job.name

if __name__ == "__main__":
    main()
