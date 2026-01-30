"""Model evaluation script for no-show prediction model.

Computes accuracy, AUC-ROC, precision, recall, and feature importance.
Uses DefaultAzureCredential for credential-less authentication.

Usage:
    python -m ml.src.evaluation.evaluate_model --job-name <automl-job-name>
"""

import argparse
import json
import logging
import os
from pathlib import Path

import matplotlib.pyplot as plt
import mlflow
import numpy as np
import pandas as pd
from azure.ai.ml import MLClient
from azure.identity import DefaultAzureCredential
from sklearn.metrics import (
    accuracy_score,
    auc,
    classification_report,
    confusion_matrix,
    precision_recall_curve,
    roc_auc_score,
    roc_curve,
)

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


def load_test_data(data_path: str, target_column: str = "no_show") -> tuple[pd.DataFrame, pd.Series]:
    """Load test data for evaluation.

    Args:
        data_path: Path to test data parquet file
        target_column: Name of target column

    Returns:
        Tuple of (features_df, target_series)
    """
    logger.info(f"Loading test data from {data_path}")
    df = pd.read_parquet(data_path)

    y = df[target_column]
    X = df.drop(columns=[target_column])

    logger.info(f"Test data: {len(df):,} records")
    logger.info(f"No-show rate: {y.mean():.1%}")

    return X, y


def get_best_model(ml_client: MLClient, job_name: str):
    """Get the best model from an AutoML job.

    Args:
        ml_client: Authenticated MLClient
        job_name: AutoML job name

    Returns:
        Model object and run info
    """
    logger.info(f"Retrieving best model from job: {job_name}")

    job = ml_client.jobs.get(job_name)

    if hasattr(job, "best_child_run_id") and job.best_child_run_id:
        best_run_id = job.best_child_run_id
        logger.info(f"Best run: {best_run_id}")

        # Download model
        best_run = ml_client.jobs.get(best_run_id)
        return best_run
    else:
        raise ValueError(f"No best run found for job {job_name}")


def download_model_artifacts(ml_client: MLClient, run_id: str, output_dir: Path) -> Path:
    """Download model artifacts from a run.

    Args:
        ml_client: Authenticated MLClient
        run_id: Run ID to download from
        output_dir: Output directory for artifacts

    Returns:
        Path to downloaded model directory
    """
    output_dir = Path(output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)

    logger.info(f"Downloading model artifacts to {output_dir}")

    # Download outputs
    ml_client.jobs.download(run_id, download_path=str(output_dir))

    # Find model file
    model_dir = output_dir / "outputs" / "model"
    if not model_dir.exists():
        model_dir = output_dir  # Fallback to root

    logger.info(f"Model artifacts downloaded to {model_dir}")
    return model_dir


def load_model_from_mlflow(tracking_uri: str, run_id: str):
    """Load model from MLflow.

    Args:
        tracking_uri: MLflow tracking URI
        run_id: Run ID

    Returns:
        Loaded model
    """
    mlflow.set_tracking_uri(tracking_uri)

    model_uri = f"runs:/{run_id}/model"
    logger.info(f"Loading model from {model_uri}")

    try:
        model = mlflow.sklearn.load_model(model_uri)
        return model
    except Exception as e:
        logger.warning(f"Could not load sklearn model: {e}")
        # Try generic pyfunc load
        return mlflow.pyfunc.load_model(model_uri)


def compute_metrics(
    y_true: np.ndarray,
    y_pred: np.ndarray,
    y_prob: np.ndarray | None = None,
) -> dict:
    """Compute classification metrics.

    Args:
        y_true: True labels
        y_pred: Predicted labels
        y_prob: Predicted probabilities (optional)

    Returns:
        Dictionary of metrics
    """
    metrics = {
        "accuracy": accuracy_score(y_true, y_pred),
        "confusion_matrix": confusion_matrix(y_true, y_pred).tolist(),
    }

    # Classification report as dict
    report = classification_report(y_true, y_pred, output_dict=True)
    metrics["precision_no_show"] = report.get("1", {}).get("precision", 0)
    metrics["recall_no_show"] = report.get("1", {}).get("recall", 0)
    metrics["f1_no_show"] = report.get("1", {}).get("f1-score", 0)

    # AUC-ROC if probabilities available
    if y_prob is not None:
        metrics["auc_roc"] = roc_auc_score(y_true, y_prob)

        # Compute ROC curve
        fpr, tpr, _ = roc_curve(y_true, y_prob)
        metrics["roc_curve"] = {"fpr": fpr.tolist(), "tpr": tpr.tolist()}

        # Compute PR curve
        precision_vals, recall_vals, _ = precision_recall_curve(y_true, y_prob)
        metrics["pr_curve"] = {
            "precision": precision_vals.tolist(),
            "recall": recall_vals.tolist(),
        }
        metrics["auc_pr"] = auc(recall_vals, precision_vals)

    return metrics


def get_feature_importance(model, feature_names: list[str]) -> dict[str, float]:
    """Extract feature importance from model.

    Args:
        model: Trained model
        feature_names: List of feature names

    Returns:
        Dictionary mapping feature name to importance
    """
    importance = {}

    if hasattr(model, "feature_importances_"):
        # Tree-based models
        raw_importance = model.feature_importances_
    elif hasattr(model, "coef_"):
        # Linear models
        raw_importance = np.abs(model.coef_).flatten()
    elif hasattr(model, "named_steps"):
        # Pipeline - try to get from final estimator
        final_estimator = list(model.named_steps.values())[-1]
        return get_feature_importance(final_estimator, feature_names)
    else:
        logger.warning("Model does not provide feature importance")
        return {}

    # Normalize to sum to 1
    total = sum(raw_importance)
    if total > 0:
        raw_importance = raw_importance / total

    # Create mapping
    for i, name in enumerate(feature_names):
        if i < len(raw_importance):
            importance[name] = float(raw_importance[i])

    # Sort by importance
    importance = dict(sorted(importance.items(), key=lambda x: x[1], reverse=True))

    return importance


def plot_evaluation_charts(
    metrics: dict,
    feature_importance: dict[str, float],
    output_dir: Path,
) -> list[Path]:
    """Generate evaluation visualization charts.

    Args:
        metrics: Computed metrics dictionary
        feature_importance: Feature importance dictionary
        output_dir: Output directory for charts

    Returns:
        List of generated chart paths
    """
    output_dir = Path(output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    charts = []

    # 1. Confusion Matrix
    if "confusion_matrix" in metrics:
        fig, ax = plt.subplots(figsize=(8, 6))
        cm = np.array(metrics["confusion_matrix"])
        im = ax.imshow(cm, cmap="Blues")
        ax.set_xticks([0, 1])
        ax.set_yticks([0, 1])
        ax.set_xticklabels(["No Show=0", "No Show=1"])
        ax.set_yticklabels(["No Show=0", "No Show=1"])
        ax.set_xlabel("Predicted")
        ax.set_ylabel("Actual")
        ax.set_title("Confusion Matrix")

        # Add text annotations
        for i in range(2):
            for j in range(2):
                ax.text(j, i, f"{cm[i, j]:,}", ha="center", va="center", fontsize=14)

        plt.colorbar(im)
        plt.tight_layout()
        path = output_dir / "confusion_matrix.png"
        plt.savefig(path, dpi=150)
        plt.close()
        charts.append(path)
        logger.info(f"Saved confusion matrix to {path}")

    # 2. ROC Curve
    if "roc_curve" in metrics:
        fig, ax = plt.subplots(figsize=(8, 6))
        fpr = metrics["roc_curve"]["fpr"]
        tpr = metrics["roc_curve"]["tpr"]
        auc_score = metrics.get("auc_roc", 0)

        ax.plot(fpr, tpr, label=f"ROC (AUC = {auc_score:.3f})", linewidth=2)
        ax.plot([0, 1], [0, 1], "k--", label="Random")
        ax.set_xlabel("False Positive Rate")
        ax.set_ylabel("True Positive Rate")
        ax.set_title("ROC Curve")
        ax.legend(loc="lower right")
        ax.grid(True, alpha=0.3)

        plt.tight_layout()
        path = output_dir / "roc_curve.png"
        plt.savefig(path, dpi=150)
        plt.close()
        charts.append(path)
        logger.info(f"Saved ROC curve to {path}")

    # 3. Precision-Recall Curve
    if "pr_curve" in metrics:
        fig, ax = plt.subplots(figsize=(8, 6))
        precision = metrics["pr_curve"]["precision"]
        recall = metrics["pr_curve"]["recall"]
        auc_pr = metrics.get("auc_pr", 0)

        ax.plot(recall, precision, label=f"PR (AUC = {auc_pr:.3f})", linewidth=2)
        ax.set_xlabel("Recall")
        ax.set_ylabel("Precision")
        ax.set_title("Precision-Recall Curve")
        ax.legend(loc="lower left")
        ax.grid(True, alpha=0.3)

        plt.tight_layout()
        path = output_dir / "pr_curve.png"
        plt.savefig(path, dpi=150)
        plt.close()
        charts.append(path)
        logger.info(f"Saved PR curve to {path}")

    # 4. Feature Importance
    if feature_importance:
        fig, ax = plt.subplots(figsize=(10, 8))
        top_features = dict(list(feature_importance.items())[:15])

        features = list(top_features.keys())
        importances = list(top_features.values())

        y_pos = np.arange(len(features))
        ax.barh(y_pos, importances, align="center")
        ax.set_yticks(y_pos)
        ax.set_yticklabels(features)
        ax.invert_yaxis()  # Top feature at top
        ax.set_xlabel("Importance")
        ax.set_title("Top 15 Feature Importance")
        ax.grid(True, axis="x", alpha=0.3)

        plt.tight_layout()
        path = output_dir / "feature_importance.png"
        plt.savefig(path, dpi=150)
        plt.close()
        charts.append(path)
        logger.info(f"Saved feature importance to {path}")

    return charts


def save_evaluation_report(
    metrics: dict,
    feature_importance: dict[str, float],
    output_path: Path,
) -> None:
    """Save evaluation report to JSON.

    Args:
        metrics: Computed metrics
        feature_importance: Feature importance
        output_path: Output file path
    """
    report = {
        "metrics": {
            "accuracy": metrics.get("accuracy"),
            "auc_roc": metrics.get("auc_roc"),
            "auc_pr": metrics.get("auc_pr"),
            "precision_no_show": metrics.get("precision_no_show"),
            "recall_no_show": metrics.get("recall_no_show"),
            "f1_no_show": metrics.get("f1_no_show"),
        },
        "confusion_matrix": metrics.get("confusion_matrix"),
        "feature_importance": feature_importance,
    }

    output_path = Path(output_path)
    output_path.parent.mkdir(parents=True, exist_ok=True)

    with open(output_path, "w") as f:
        json.dump(report, f, indent=2)

    logger.info(f"Saved evaluation report to {output_path}")


def log_to_mlflow(
    ml_client: MLClient,
    metrics: dict,
    feature_importance: dict[str, float],
    charts: list[Path],
) -> None:
    """Log evaluation results to MLflow.

    Args:
        ml_client: Authenticated MLClient
        metrics: Computed metrics
        feature_importance: Feature importance
        charts: List of chart paths
    """
    # Set tracking URI
    tracking_uri = ml_client.workspaces.get(ml_client.workspace_name).mlflow_tracking_uri
    mlflow.set_tracking_uri(tracking_uri)
    mlflow.set_experiment("noshow-evaluation")

    with mlflow.start_run(run_name="model-evaluation"):
        # Log metrics
        mlflow.log_metric("accuracy", metrics.get("accuracy", 0))
        mlflow.log_metric("auc_roc", metrics.get("auc_roc", 0))
        mlflow.log_metric("auc_pr", metrics.get("auc_pr", 0))
        mlflow.log_metric("precision", metrics.get("precision_no_show", 0))
        mlflow.log_metric("recall", metrics.get("recall_no_show", 0))
        mlflow.log_metric("f1", metrics.get("f1_no_show", 0))

        # Log feature importance as params (top 10)
        for i, (feature, importance) in enumerate(list(feature_importance.items())[:10]):
            mlflow.log_param(f"feature_{i+1}_name", feature)
            mlflow.log_metric(f"feature_{i+1}_importance", importance)

        # Log charts as artifacts
        for chart_path in charts:
            mlflow.log_artifact(str(chart_path))

        logger.info("Logged evaluation to MLflow")


def evaluate_from_predictions(
    predictions_path: str,
    output_dir: str = "./outputs/evaluation",
) -> dict:
    """Evaluate model from saved predictions file.

    Args:
        predictions_path: Path to predictions CSV/parquet with y_true, y_pred, y_prob columns
        output_dir: Output directory

    Returns:
        Metrics dictionary
    """
    logger.info(f"Loading predictions from {predictions_path}")

    if predictions_path.endswith(".parquet"):
        df = pd.read_parquet(predictions_path)
    else:
        df = pd.read_csv(predictions_path)

    y_true = df["y_true"].values
    y_pred = df["y_pred"].values
    y_prob = df.get("y_prob", df.get("y_prob_1"))
    if y_prob is not None:
        y_prob = y_prob.values

    # Compute metrics
    metrics = compute_metrics(y_true, y_pred, y_prob)

    logger.info("=== Evaluation Results ===")
    logger.info(f"Accuracy: {metrics['accuracy']:.3f}")
    if "auc_roc" in metrics:
        logger.info(f"AUC-ROC: {metrics['auc_roc']:.3f}")
    logger.info(f"Precision (No-Show): {metrics['precision_no_show']:.3f}")
    logger.info(f"Recall (No-Show): {metrics['recall_no_show']:.3f}")
    logger.info(f"F1 (No-Show): {metrics['f1_no_show']:.3f}")

    # Generate charts
    charts = plot_evaluation_charts(metrics, {}, Path(output_dir))

    # Save report
    save_evaluation_report(metrics, {}, Path(output_dir) / "evaluation_report.json")

    return metrics


def main(
    job_name: str | None = None,
    predictions_path: str | None = None,
    output_dir: str = "./outputs/evaluation",
) -> None:
    """Main evaluation pipeline.

    Args:
        job_name: AutoML job name to evaluate
        predictions_path: Path to predictions file (alternative to job_name)
        output_dir: Output directory for results
    """
    logger.info("=== Model Evaluation ===")

    if predictions_path:
        # Evaluate from predictions file
        evaluate_from_predictions(predictions_path, output_dir)
    elif job_name:
        # Evaluate from AutoML job
        ml_client = get_ml_client()

        # Get best model
        best_run = get_best_model(ml_client, job_name)

        # Load test data
        test_data_path = os.environ.get("TEST_DATA_PATH", "./data/ml_prepared/test.parquet")
        X_test, y_test = load_test_data(test_data_path)

        # Load model from MLflow
        tracking_uri = ml_client.workspaces.get(ml_client.workspace_name).mlflow_tracking_uri
        model = load_model_from_mlflow(tracking_uri, best_run.name)

        # Make predictions
        logger.info("Generating predictions...")
        y_pred = model.predict(X_test)
        y_prob = None
        if hasattr(model, "predict_proba"):
            y_prob = model.predict_proba(X_test)[:, 1]

        # Compute metrics
        metrics = compute_metrics(y_test.values, y_pred, y_prob)

        # Get feature importance
        feature_importance = get_feature_importance(model, list(X_test.columns))

        # Log results
        logger.info("=== Evaluation Results ===")
        logger.info(f"Accuracy: {metrics['accuracy']:.3f}")
        if "auc_roc" in metrics:
            logger.info(f"AUC-ROC: {metrics['auc_roc']:.3f}")
        logger.info(f"Precision (No-Show): {metrics['precision_no_show']:.3f}")
        logger.info(f"Recall (No-Show): {metrics['recall_no_show']:.3f}")

        if feature_importance:
            logger.info("\nTop 5 Features:")
            for feature, importance in list(feature_importance.items())[:5]:
                logger.info(f"  {feature}: {importance:.3f}")

        # Generate charts
        charts = plot_evaluation_charts(metrics, feature_importance, Path(output_dir))

        # Save report
        save_evaluation_report(metrics, feature_importance, Path(output_dir) / "evaluation_report.json")

        # Log to MLflow
        log_to_mlflow(ml_client, metrics, feature_importance, charts)
    else:
        raise ValueError("Either job_name or predictions_path must be provided")

    logger.info("=== Evaluation Complete ===")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Evaluate no-show prediction model")
    parser.add_argument(
        "--job-name",
        type=str,
        help="AutoML job name to evaluate",
    )
    parser.add_argument(
        "--predictions",
        type=str,
        help="Path to predictions file (alternative to job-name)",
    )
    parser.add_argument(
        "--output-dir",
        type=str,
        default="./outputs/evaluation",
        help="Output directory for evaluation results",
    )

    args = parser.parse_args()

    main(
        job_name=args.job_name,
        predictions_path=args.predictions,
        output_dir=args.output_dir,
    )
