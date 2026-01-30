# No-Show Predictor ML Pipeline

Python-based machine learning pipeline for the Medical Appointment No-Show Predictor.

## Overview

This component handles:
- Synthetic patient/appointment data generation
- Azure AutoML model training for no-show classification
- Model evaluation and deployment to managed endpoints

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
│   │   ├── seed_database.py      # Load data into Azure SQL
│   │   └── schema.py             # Data schema definitions
│   ├── training/                 # Model training
│   │   ├── train_automl.py       # AutoML job submission
│   │   └── config.yaml           # AutoML configuration
│   └── evaluation/               # Model evaluation
│       └── evaluate_model.py     # Metrics and validation
├── tests/
│   └── test_synthetic_data.py    # Data validation tests
├── requirements.txt              # Python dependencies (pinned)
└── README.md
```

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

### Generate Synthetic Data

```bash
python -m ml.src.data.generate_synthetic --output-dir ml/data/synthetic --num-patients 500 --num-providers 30 --num-appointments 2000
```

Output files in `ml/data/synthetic/`:
- `patients.parquet` - Synthetic patients
- `providers.parquet` - Providers  
- `departments.parquet` - Departments
- `appointments.parquet` - Appointments with no-show labels
- `insurance.parquet` - Insurance records

### Prepare ML Training Data

The prepared MLTable data is committed at `ml/data/ml_prepared/`. To regenerate:

```bash
# Transform raw data to ML features (if needed)
python -m ml.src.data.prepare_ml_data
```

### Upload Data to Azure ML

```bash
# Upload MLTable data as versioned data asset
az ml data create \
  --name noshow-training-data \
  --version 1 \
  --type mltable \
  --path ml/data/ml_prepared \
  -g <resource-group> \
  -w <workspace-name>
```

### Submit Training Job

```bash
az ml job create \
  -f ml/src/training/automl_job.yaml \
  -g <resource-group> \
  -w <workspace-name>
```

Or via Python SDK:

### Evaluate Model

```bash
python src/evaluation/evaluate_model.py --model-name noshow-classifier
```

## Environment Variables

| Variable | Description |
|----------|-------------|
| `AZURE_SUBSCRIPTION_ID` | Azure subscription |
| `AZURE_ML_WORKSPACE_NAME` | ML workspace name |
| `AZURE_ML_RESOURCE_GROUP` | Resource group |
| `SQL_CONNECTION_STRING` | Azure SQL connection |

## Data Generation Rules

### No-Show Probability Factors
- **Increases risk**: Previous no-shows (+0.25), long lead time (+0.10), Medicaid/Self-Pay (+0.08)
- **Decreases risk**: Perfect attendance (-0.15), short lead time (-0.08), virtual visits (-0.05)
- **Base rate**: ~22% (typical outpatient)

### Patient Journey Patterns
| Pattern | Frequency |
|---------|-----------|
| Routine care | 40% |
| Chronic management | 25% |
| Episodic | 20% |
| Referral chain | 10% |
| Care abandonment | 5% |

### Seasonality
| Period | No-Show Modifier |
|--------|------------------|
| Dec 20-Jan 5 | +15% |
| Jul 1-Aug 15 | +8% |
| Mon after holiday | +10% |

## Testing

```bash
pytest tests/
```

## Related Documentation

- [Specification](../specs/001-no-show-predictor/spec.md)
- [Data Model](../specs/001-no-show-predictor/data-model.md)
- [Research](../specs/001-no-show-predictor/research.md)
- [ML Inference Contract](../specs/001-no-show-predictor/contracts/ml-inference.openapi.yaml)
