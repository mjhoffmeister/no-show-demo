# No-Show Predictor ML Pipeline

Python-based machine learning pipeline for the Medical Appointment No-Show Predictor.

## Overview

The model predicts whether a patient will miss their scheduled appointment (no-show), enabling healthcare organizations to:
- Target high-risk patients with reminder interventions
- Implement strategic overbooking policies
- Reduce revenue loss from empty appointment slots

## Technology Stack

| Component | Technology |
|-----------|------------|
| ML Platform | Azure Machine Learning |
| Training | Azure AutoML (classification) |
| Hosting | Azure ML Managed Online Endpoints |
| SDK | azure-ai-ml 1.31.0 |
| Tracking | MLflow 3.8.1 |
| Python | 3.11.x |

## Project Structure

```
ml/
├── src/
│   ├── data/                     # Data generation
│   │   ├── generate_synthetic.py # Synthetic data generator
│   │   ├── prepare_ml_data.py    # Feature engineering
│   │   ├── seed_database.py      # Load data into Azure SQL
│   │   └── schema.py             # Data schema definitions
│   ├── training/                 # Model training
│   │   ├── submit_automl_v2.py   # AutoML job submission
│   │   └── config.yaml           # AutoML configuration
│   ├── deployment/               # Model deployment
│   │   └── deploy_model.py       # Automated deployment script
│   └── evaluation/               # Model evaluation
│       └── evaluate_model.py     # Metrics and validation
├── deployment/                   # Deployment config files
│   ├── endpoint.yaml             # Endpoint configuration
│   └── deployment.yaml           # Deployment configuration
├── data/
│   ├── ml_prepared/              # Training MLTable
│   └── ml_test/                  # Holdout test MLTable
├── tests/
│   └── test_synthetic_data.py    # Data validation tests
└── requirements.txt              # Python dependencies (pinned)
```

---

## Local Development

### Prerequisites

- Python 3.11.x
- Azure CLI (`az login`)
- Access to Azure ML workspace

### Setup

```bash
# Create virtual environment
python -m venv .venv

# Activate (Windows)
.venv\Scripts\activate

# Activate (Linux/macOS)
source .venv/bin/activate

# Install dependencies
pip install -r requirements.txt
```

### Environment Variables

| Variable | Description |
|----------|-------------|
| `AZURE_SUBSCRIPTION_ID` | Azure subscription |
| `AZURE_RESOURCE_GROUP` | Resource group |
| `AZURE_ML_WORKSPACE` | ML workspace name |
| `SQL_CONNECTION_STRING` | Azure SQL connection |

---

## Synthetic Data Generation

### Why Synthetic Data?

Real patient data is protected by HIPAA and requires extensive de-identification. Synthetic data allows:
- Rapid prototyping without compliance overhead
- Reproducible experiments
- Scalable dataset generation

### Generate Data

```bash
python -m ml.src.data.generate_synthetic --output-dir ml/data/synthetic --num-patients 500 --num-providers 30 --num-appointments 2000
```

Output files in `ml/data/synthetic/`:
- `patients.parquet` - Synthetic patients
- `providers.parquet` - Providers  
- `departments.parquet` - Departments
- `appointments.parquet` - Appointments with no-show labels
- `insurance.parquet` - Insurance records

### Prepare ML Features

```bash
python -m ml.src.data.prepare_ml_data
```

### Benchmark: Kaggle No-Show Dataset

We calibrated our synthetic data against the [Kaggle No-Show Dataset](https://www.kaggle.com/datasets/joniarroba/noshowappointments) (110,527 Brazilian medical appointments) to ensure realistic correlation patterns.

### No-Show Probability Factors

Probability modifiers are calibrated to match real-world patterns:

| Factor | Effect | Rationale |
|--------|--------|-----------|
| **Lead time** | Same-day: -0.12, 30+ days: +0.16 | Kaggle shows 6.6% → 33% no-show as lead time increases |
| **Age** | Young adult: +0.04, Senior: -0.05 | 18-40 age group has highest no-show rate |
| **History** | Up to +0.20 based on past no-shows | Past behavior predicts future behavior |
| **Medicaid** | +0.06 | Transportation/work barriers |
| **Self-pay** | +0.08 | Cost concerns |
| **Medicare** | -0.03 | Retired, fewer conflicts |

**Target no-show rate:** ~20% (matching Kaggle's 20.2%)

---

## Model Training

### Upload Data to Azure ML

```bash
# Training data
az ml data create \
  --name noshow-training-data \
  --version 3 \
  --type mltable \
  --path ml/data/ml_prepared \
  -g <resource-group> \
  -w <workspace-name>

# Test data (holdout)
az ml data create \
  --name noshow-test-data \
  --version 2 \
  --type mltable \
  --path ml/data/ml_test \
  -g <resource-group> \
  -w <workspace-name>
```

### Submit AutoML Job

```bash
export AZURE_SUBSCRIPTION_ID=<subscription-id>
export AZURE_RESOURCE_GROUP=<resource-group>
export AZURE_ML_WORKSPACE=<workspace-name>
python -m ml.src.training.submit_automl_v2
```

### Training Configuration

```python
classification_job = automl.classification(
    training_data=training_data,
    test_data=test_data,
    target_column_name="no_show",
    primary_metric=ClassificationPrimaryMetrics.NORM_MACRO_RECALL,
    positive_label=True,
    enable_model_explainability=True,
    n_cross_validations=5,
)
```

### Key Design Decisions

#### Primary Metric: NORM_MACRO_RECALL

**Problem:** With ~80% "Show" / ~20% "No-Show" class imbalance, models optimizing accuracy simply predict all appointments as "Show" (80% accuracy but 0% no-show detection).

**Solution:** `NORM_MACRO_RECALL` penalizes models that ignore the minority class:
- Formula: (recall_class0 + recall_class1) / 2, normalized to [0,1]
- A model predicting all "Show" scores **0.0** on this metric
- Forces AutoML to find algorithms that detect no-shows

#### Cross-Validation + Holdout Test

- 5-fold cross-validation during training (model selection)
- Separate 20% holdout test set (unbiased evaluation)

#### Data Versioning

| Version | Description |
|---------|-------------|
| v1 | Initial 5 features |
| v2 | Enriched with demographics, lead time, history |
| v3 | Recalibrated correlations to match Kaggle patterns |

---

## Expected Performance

### Realistic Benchmarks

| AUC Range | Interpretation |
|-----------|----------------|
| 0.50 | Random guess |
| 0.60-0.70 | Poor to Fair |
| 0.70-0.80 | Acceptable for operations |
| 0.80+ | Good (rare for behavioral prediction) |

**Target:** AUC 0.68-0.72 with balanced accuracy > 0.60

### Why Perfect Prediction is Impossible

No-show behavior depends on unobservable factors:
- Day-of illness or emergencies
- Traffic/weather conditions
- Childcare or work conflicts

Published literature shows **0.75-0.80 AUC as the practical ceiling** for this problem domain.

---

## Operational Value

Even a "fair" model (AUC ~0.70) provides material value:

| Intervention | Model's Role |
|--------------|--------------|
| Targeted reminders | Route calls to top 20% risk patients |
| Overbooking | Book 2 patients for high-risk slots |
| Waitlist management | Fill no-show slots from cancellation list |

**ROI:** Cost of empty slot ($150-300) vs. cost of reminder call ($2-5) = significant revenue recovery with even 10-15% no-show reduction.

---

## Model Deployment

### Automated Deployment Script

Deploy a trained model to Azure ML managed endpoint:

```bash
# Set environment variables
export AZURE_SUBSCRIPTION_ID=<subscription-id>
export AZURE_RESOURCE_GROUP=<resource-group>
export AZURE_ML_WORKSPACE=<workspace-name>

# Deploy best model from AutoML job (auto-detects best child run)
python -m ml.src.deployment.deploy_model --job-name <automl-job-name>

# Or specify the child run if you know it
python -m ml.src.deployment.deploy_model \
  --job-name calm_bulb_0gtr3bskvp \
  --child-run-suffix 3 \
  --model-version 2
```

### Deployment Options

| Option | Default | Description |
|--------|---------|-------------|
| `--job-name` | (required) | AutoML parent job name |
| `--child-run-suffix` | auto-detect | Child run suffix (e.g., 3) |
| `--model-name` | noshow-predictor | Model registry name |
| `--model-version` | 1 | Model version |
| `--endpoint-name` | noshow-predictor | Endpoint name |
| `--deployment-name` | noshow-model-v1 | Deployment name |
| `--skip-registration` | false | Use existing registered model |

### What the Script Does

1. **Registers model** from AutoML job outputs to Azure ML Model Registry
2. **Creates endpoint** if it doesn't exist (uses AAD token auth)
3. **Deploys model** using MLflow no-code deployment
4. **Routes traffic** 100% to the new deployment

### Manual Deployment (Alternative)

```bash
# Register model
az ml model create \
  --name noshow-predictor \
  --version 1 \
  --path "azureml://jobs/<child-run-name>/outputs/artifacts/outputs/mlflow-model" \
  --type mlflow_model \
  -g <resource-group> \
  -w <workspace>

# Create endpoint
az ml online-endpoint create --file ml/deployment/endpoint.yaml

# Create deployment
az ml online-deployment create --file ml/deployment/deployment.yaml

# Set traffic
az ml online-endpoint update \
  --name noshow-predictor \
  --traffic "noshow-model-v1=100" \
  -g <resource-group> \
  -w <workspace>
```

---

## Evaluate Model

```bash
python src/evaluation/evaluate_model.py --model-name noshow-classifier
```

## Testing

```bash
pytest tests/
```

---

## Future Improvements

1. **Real data integration** - Replace synthetic with de-identified historical data
2. **Additional features** - SMS confirmation responses, appointment type, weather
3. **Continuous retraining** - Monitor drift and retrain as patterns shift
4. **Threshold optimization** - Tune classification threshold for business cost function

---

## Related Documentation

- [Specification](../specs/001-no-show-predictor/spec.md)
- [Data Model](../specs/001-no-show-predictor/data-model.md)
- [Research](../specs/001-no-show-predictor/research.md)
- [ML Inference Contract](../specs/001-no-show-predictor/contracts/ml-inference.openapi.yaml)
