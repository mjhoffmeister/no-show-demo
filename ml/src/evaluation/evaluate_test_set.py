"""
Evaluate the trained model on the unseen test set.
"""

import pandas as pd
import pickle
from pathlib import Path
from sklearn.metrics import roc_auc_score, confusion_matrix


def main():
    # Load test data
    test = pd.read_parquet("ml/data/ml_prepared/test.parquet")
    print(f"Test set: {len(test)} rows")
    print(f"Test no-show rate: {test['no_show'].mean():.1%}")

    X_test = test.drop("no_show", axis=1)
    y_test = test["no_show"]

    # Find model file
    model_path = Path("model_artifacts_18/named-outputs/mlflow_log_model_1153353413/model.pkl")
    if not model_path.exists():
        # Try alternative path
        model_path = Path("model_artifacts_18/artifacts/outputs/model.pkl")
    
    print(f"\nLoading model from: {model_path}")
    with open(model_path, "rb") as f:
        model = pickle.load(f)

    # Get predictions
    y_pred_proba = model.predict_proba(X_test)[:, 1]
    y_pred = (y_pred_proba >= 0.5).astype(int)

    # Calculate metrics
    auc = roc_auc_score(y_test, y_pred_proba)
    cm = confusion_matrix(y_test, y_pred)

    print(f"\n{'='*50}")
    print(f"TEST SET RESULTS (Unseen Data)")
    print(f"{'='*50}")
    print(f"AUC: {auc:.4f}")
    print(f"\nConfusion Matrix (threshold=0.5):")
    print(f"              Predicted")
    print(f"              Show   No-Show")
    print(f"Actual Show   {cm[0, 0]:>5}  {cm[0, 1]:>5}")
    print(f"Actual No-Show{cm[1, 0]:>5}  {cm[1, 1]:>5}")

    # Try different thresholds
    print(f"\n{'='*50}")
    print("Performance at Different Risk Thresholds")
    print(f"{'='*50}")
    
    for thresh in [0.20, 0.25, 0.30, 0.35, 0.40]:
        y_pred_t = (y_pred_proba >= thresh).astype(int)
        cm_t = confusion_matrix(y_test, y_pred_t)
        
        # cm_t: [[TN, FP], [FN, TP]]
        tn, fp, fn, tp = cm_t[0, 0], cm_t[0, 1], cm_t[1, 0], cm_t[1, 1]
        
        recall = tp / (fn + tp) if (fn + tp) > 0 else 0
        precision = tp / (fp + tp) if (fp + tp) > 0 else 0
        
        print(f"\nThreshold {thresh}:")
        print(f"  Recall (catch rate): {recall:.1%} of no-shows identified")
        print(f"  Precision: {precision:.1%} of flagged are actual no-shows")
        print(f"  Catches {tp} of {fn + tp} no-shows, with {fp} false alarms")

    # Distribution of probabilities
    print(f"\n{'='*50}")
    print("Probability Distribution")
    print(f"{'='*50}")
    print(f"Min: {y_pred_proba.min():.3f}")
    print(f"25th percentile: {pd.Series(y_pred_proba).quantile(0.25):.3f}")
    print(f"Median: {pd.Series(y_pred_proba).quantile(0.50):.3f}")
    print(f"75th percentile: {pd.Series(y_pred_proba).quantile(0.75):.3f}")
    print(f"Max: {y_pred_proba.max():.3f}")


if __name__ == "__main__":
    main()
