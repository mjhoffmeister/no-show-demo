# No-Show Prediction Model Development Guide

This document explains the synthetic data generation strategy and model training decisions for the medical appointment no-show predictor.

## Overview

The model predicts whether a patient will miss their scheduled appointment (no-show), enabling healthcare organizations to:
- Target high-risk patients with reminder interventions
- Implement strategic overbooking policies
- Reduce revenue loss from empty appointment slots

---

## Synthetic Data Generation

### Why Synthetic Data?

Real patient data is protected by HIPAA and requires extensive de-identification. Synthetic data allows:
- Rapid prototyping without compliance overhead
- Reproducible experiments
- Scalable dataset generation

### Benchmark: Kaggle Medical Appointment No-Shows

We calibrated our synthetic data against the [Kaggle No-Show Dataset](https://www.kaggle.com/datasets/joniarroba/noshowappointments) (110,527 Brazilian medical appointments), which provides realistic correlation patterns.

**Key findings from Kaggle:**

| Feature | Correlation with No-Show |
|---------|--------------------------|
| Lead time (days) | +0.186 |
| Age | -0.060 |
| SMS received | -0.099 |

### Calibration Decisions

#### 1. Lead Time Effect (Strongest Signal)

**Observation:** Kaggle shows appointments scheduled further in advance have significantly higher no-show rates.

| Lead Time | Kaggle No-Show Rate |
|-----------|---------------------|
| Same-day | 6.6% |
| 8-14 days | 31.2% |
| 30+ days | 33.0% |

**Implementation:** We apply progressive probability modifiers in `generate_synthetic.py`:

```python
# Lead time modifiers (calibrated to Kaggle)
if lead_time_days == 0:
    modifier -= 0.12  # Same-day: much lower risk
elif lead_time_days <= 3:
    modifier -= 0.06
elif lead_time_days <= 7:
    modifier += 0.04
elif lead_time_days <= 14:
    modifier += 0.10
elif lead_time_days <= 30:
    modifier += 0.14
else:
    modifier += 0.16  # 30+ days: highest risk
```

#### 2. Age Effect

**Observation:** Younger patients (especially young adults) have higher no-show rates; seniors are more reliable.

| Age Group | Kaggle No-Show Rate |
|-----------|---------------------|
| 0-18 | 22.5% |
| 18-40 | 23.2% |
| 40-65 | 17.9% |
| 65+ | 15.5% |

**Implementation:**
```python
if age < 18:
    modifier += 0.03  # Pediatric (guardian-dependent)
elif age < 40:
    modifier += 0.04  # Young adults: highest risk
elif age < 65:
    modifier -= 0.02  # Middle-aged
else:
    modifier -= 0.05  # Seniors: most reliable
```

#### 3. Historical Behavior (Domain Knowledge)

**Rationale:** Past behavior is the best predictor of future behavior. Patients with prior no-shows are significantly more likely to miss again.

**Implementation:** Scaled impact based on historical no-show rate:
```python
history_impact = min(0.20, historical_rate * 0.5)
modifier += history_impact
```

#### 4. Insurance Type (US Healthcare Context)

**Rationale:** Payer type correlates with socioeconomic factors affecting appointment adherence.

| Payer | Modifier | Reasoning |
|-------|----------|-----------|
| Medicaid | +0.06 | Transportation/work barriers |
| Self-pay | +0.08 | Cost concerns may cause last-minute cancellations |
| Medicare | -0.03 | Retired, fewer scheduling conflicts |
| Commercial | Baseline | Reference category |

#### 5. Base Rate Calibration

**Target:** ~20% overall no-show rate (matching Kaggle's 20.2%)

**Process:** Iteratively adjusted `BASE_NO_SHOW_RATE` constant:
- Started at 0.18 → resulted in 24% (too high)
- Adjusted to 0.08 → resulted in 22%
- Final: -0.02 → achieved 20.2%

The negative base rate is counterintuitive but correct—the various positive modifiers (lead time, age, history) provide sufficient uplift to reach the target distribution.

---

## Model Training

### Platform: Azure Machine Learning AutoML

**Why AutoML?**
- Automatically explores multiple algorithms (LightGBM, XGBoost, Random Forest, etc.)
- Handles feature engineering (encoding, normalization)
- Built-in cross-validation and hyperparameter tuning
- Provides model explainability out of the box

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
    compute="cpu-cluster",
)
```

### Key Decisions

#### 1. Primary Metric: NORM_MACRO_RECALL

**Problem:** With ~80% "Show" / ~20% "No-Show" class imbalance, models optimizing accuracy simply predict all appointments as "Show" (achieving 80% accuracy but 0% no-show detection).

**Evidence:** Initial model with AUC_WEIGHTED metric:
- Accuracy: 79.8%
- Balanced Accuracy: 50.4% (≈ random)
- Matthews Correlation: 0.047 (near-zero)

**Solution:** `NORM_MACRO_RECALL` penalizes models that ignore the minority class:
- Formula: (recall_class0 + recall_class1) / 2, normalized to [0,1]
- A model predicting all "Show" scores **0.0** on this metric
- Forces AutoML to find threshold/algorithm combinations that detect no-shows

#### 2. Positive Label Specification

```python
positive_label=True  # no_show=True is the class we want to detect
```

Ensures metrics like precision/recall are computed for the no-show class, which is the actionable prediction.

#### 3. Cross-Validation + Holdout Test

**Setup:**
- 5-fold cross-validation during training (for model selection)
- Separate 20% holdout test set (for final unbiased evaluation)

**Rationale:** CV alone can overfit to training data distribution. Holdout test provides honest generalization estimate.

#### 4. Data Versioning

| Version | Description |
|---------|-------------|
| v1 | Initial 5 features |
| v2 | Enriched with patient demographics, lead time, history |
| v3 | Recalibrated correlations to match Kaggle patterns |

Each version is registered as an Azure ML Data Asset for reproducibility.

---

## Expected Performance

### Realistic Benchmarks for No-Show Prediction

| AUC Range | Interpretation |
|-----------|----------------|
| 0.50 | Random guess |
| 0.60-0.70 | Poor to Fair |
| 0.70-0.80 | Acceptable for operations |
| 0.80+ | Good (rare for behavioral prediction) |

**Our target:** AUC 0.68-0.72 with balanced accuracy > 0.60

### Why Perfect Prediction is Impossible

No-show behavior depends on factors models cannot observe:
- Day-of illness or emergencies
- Traffic/weather conditions
- Childcare or work conflicts
- Forgetfulness

Published literature consistently shows **0.75-0.80 AUC as the practical ceiling** for this problem domain, even with rich real-world data.

---

## Operational Value

Even a "fair" model (AUC ~0.70) provides material value:

| Intervention | Model's Role |
|--------------|--------------|
| Targeted reminders | Route calls to top 20% risk patients |
| Overbooking | Book 2 patients when top-risk slot, recover ~60% of otherwise-lost revenue |
| Waitlist management | Fill no-show slots from cancellation list |

**ROI calculation:**
- Cost of empty slot: $150-300
- Cost of reminder call: $2-5
- Even 10-15% reduction in no-shows = significant revenue recovery

---

## File Reference

| File | Purpose |
|------|---------|
| `ml/src/data/generate_synthetic.py` | Synthetic appointment data generator |
| `ml/src/data/prepare_ml_data.py` | Feature engineering and train/test split |
| `ml/src/training/submit_automl_v2.py` | AutoML job submission |
| `ml/data/ml_prepared/` | Training MLTable |
| `ml/data/ml_test/` | Holdout test MLTable |

---

## Future Improvements

1. **Real data integration** - Replace synthetic with de-identified historical data
2. **Additional features** - SMS confirmation responses, appointment type, weather
3. **Continuous retraining** - Monitor drift and retrain as patterns shift
4. **Threshold optimization** - Tune classification threshold for business cost function
