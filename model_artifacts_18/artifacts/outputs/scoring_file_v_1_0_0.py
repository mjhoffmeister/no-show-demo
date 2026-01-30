# ---------------------------------------------------------
# Copyright (c) Microsoft Corporation. All rights reserved.
# ---------------------------------------------------------
import json
import logging
import os
import pickle
import numpy as np
import pandas as pd
import joblib

import azureml.automl.core
from azureml.automl.core.shared import logging_utilities, log_server
from azureml.telemetry import INSTRUMENTATION_KEY

from inference_schema.schema_decorators import input_schema, output_schema
from inference_schema.parameter_types.numpy_parameter_type import NumpyParameterType
from inference_schema.parameter_types.pandas_parameter_type import PandasParameterType
from inference_schema.parameter_types.standard_py_parameter_type import StandardPythonParameterType

input_sample = pd.DataFrame({"patient_age_bucket": pd.Series(["example_value"], dtype="object"), "patient_gender": pd.Series(["example_value"], dtype="object"), "patient_zip_code": pd.Series(["example_value"], dtype="object"), "patient_race_ethnicity": pd.Series(["example_value"], dtype="object"), "portal_engaged": pd.Series([0], dtype="int64"), "historical_no_show_rate": pd.Series([0.0], dtype="float64"), "historical_no_show_count": pd.Series([0], dtype="int64"), "sipg2": pd.Series(["example_value"], dtype="object"), "lead_time_days": pd.Series([0], dtype="int64"), "appointmenttypename": pd.Series(["example_value"], dtype="object"), "virtual_flag": pd.Series(["example_value"], dtype="object"), "new_patient_flag": pd.Series(["example_value"], dtype="object"), "day_of_week": pd.Series([0], dtype="int32"), "hour_of_day": pd.Series([0], dtype="int64"), "appointmentduration": pd.Series([0], dtype="int64"), "webschedulableyn": pd.Series([0], dtype="int64"), "provider_specialty": pd.Series(["example_value"], dtype="object"), "providertype": pd.Series(["example_value"], dtype="object"), "departmentspecialty": pd.Series(["example_value"], dtype="object"), "placeofservicetype": pd.Series(["example_value"], dtype="object"), "market": pd.Series(["example_value"], dtype="object")})
output_sample = np.array([0])
method_sample = StandardPythonParameterType("predict")

try:
    log_server.enable_telemetry(INSTRUMENTATION_KEY)
    log_server.set_verbosity('INFO')
    logger = logging.getLogger('azureml.automl.core.scoring_script')
except:
    pass


def init():
    global model
    # This name is model.id of model that we want to deploy deserialize the model file back
    # into a sklearn model
    model_path = os.path.join(os.getenv('AZUREML_MODEL_DIR'), 'model.pkl')
    path = os.path.normpath(model_path)
    path_split = path.split(os.sep)
    log_server.update_custom_dimensions({'model_name': path_split[-3], 'model_version': path_split[-2]})
    try:
        logger.info("Loading model from path.")
        model = joblib.load(model_path)
        logger.info("Loading successful.")
    except Exception as e:
        logging_utilities.log_traceback(e, logger)
        raise

@input_schema('method', method_sample, convert_to_provided_type=False)
@input_schema('data', PandasParameterType(input_sample))
@output_schema(NumpyParameterType(output_sample))
def run(data, method="predict"):
    try:
        if method == "predict_proba":
            result = model.predict_proba(data)
        elif method == "predict":
            result = model.predict(data)
        else:
            raise Exception(f"Invalid predict method argument received ({method})")
        if isinstance(result, pd.DataFrame):
            result = result.values
        return json.dumps({"result": result.tolist()})
    except Exception as e:
        result = str(e)
        return json.dumps({"error": result})
