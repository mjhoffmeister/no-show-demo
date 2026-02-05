using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using NoShowPredictor.Agent.Models;

namespace NoShowPredictor.Agent.Services;

/// <summary>
/// Implementation of ML endpoint client using DefaultAzureCredential.
/// Calls Azure ML managed online endpoint for no-show predictions.
/// Uses MLflow/AutoML input format with dataframe_split schema.
/// </summary>
public class MLEndpointClient : IMLEndpointClient
{
    private readonly HttpClient _httpClient;
    private readonly TokenCredential _credential;
    private readonly string _endpointUri;
    private readonly string _modelVersion;
    private readonly JsonSerializerOptions _jsonOptions;

    // Column names matching the training data schema
    private static readonly string[] FeatureColumns =
    [
        "patient_age_bucket", "patient_gender", "patient_zip_code", "patient_race_ethnicity",
        "portal_engaged", "historical_no_show_rate", "historical_no_show_count", "sipg2",
        "lead_time_days", "appointmenttypename", "virtual_flag", "new_patient_flag",
        "day_of_week", "hour_of_day", "appointmentduration", "webschedulableyn",
        "provider_specialty", "providertype", "departmentspecialty", "placeofservicetype", "market"
    ];

    public MLEndpointClient(string endpointUri, TokenCredential? credential = null, string modelVersion = "noshow-v1.0.0")
    {
        _endpointUri = endpointUri;
        _credential = credential ?? new DefaultAzureCredential();
        _modelVersion = modelVersion;
        _httpClient = new HttpClient();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task<IReadOnlyList<Prediction>> GetPredictionsAsync(
        IEnumerable<Appointment> appointments,
        CancellationToken cancellationToken = default)
    {
        var appointmentList = appointments.ToList();
        if (appointmentList.Count == 0)
        {
            return [];
        }

        // Transform appointments to MLflow dataframe_split format with predict_proba
        var request = new MLflowRequest
        {
            InputData = new DataFrameSplit
            {
                Columns = FeatureColumns,
                Data = appointmentList.Select(CreateFeatureRow).ToList()
            },
            // Request predict_proba instead of predict to get confidence scores
            Params = new Dictionary<string, string> { { "predict_method", "predict_proba" } }
        };

        try
        {
            // Get access token for ML endpoint
            var tokenRequest = new TokenRequestContext(["https://ml.azure.com/.default"]);
            var token = await _credential.GetTokenAsync(tokenRequest, cancellationToken);

            // Use endpoint URI directly - it already includes /score from Azure's scoringUri
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpointUri);
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
            httpRequest.Content = JsonContent.Create(request, options: _jsonOptions);

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            Console.WriteLine($"ML response: {responseText}");

            // Try to parse as predict_proba format: [[p0, p1], [p0, p1], ...]
            // where p1 is the no-show probability (class 1)
            try
            {
                var probabilities = JsonSerializer.Deserialize<double[][]>(responseText);
                if (probabilities != null && probabilities.Length == appointmentList.Count)
                {
                    return appointmentList.Zip(probabilities, (appt, probs) => new Prediction
                    {
                        AppointmentId = appt.AppointmentId,
                        // probs[1] > 0.5 means model predicts no-show
                        PredictedNoShow = probs.Length > 1 ? probs[1] > 0.5 : probs[0] > 0.5,
                        RiskFactors = GetRiskFactorsForAppointment(appt, probs.Length > 1 && probs[1] > 0.5 ? 1 : 0),
                        ModelVersion = _modelVersion,
                        PredictedAt = DateTime.UtcNow
                    }).ToList();
                }
            }
            catch (JsonException)
            {
                // Fall through to try other formats
            }

            // Try to parse as new custom scoring format with probabilities
            try
            {
                var mlResponse = JsonSerializer.Deserialize<MLPredictionResponse>(responseText);
                if (mlResponse?.Predictions != null && mlResponse.Predictions.Count == appointmentList.Count)
                {
                    return appointmentList.Zip(mlResponse.Predictions, (appt, pred) => new Prediction
                    {
                        AppointmentId = appt.AppointmentId,
                        PredictedNoShow = pred.NoShowProbability > 0.5m,
                        RiskFactors = GetRiskFactorsForAppointment(appt, pred.NoShowProbability > 0.5m ? 1 : 0),
                        ModelVersion = _modelVersion,
                        PredictedAt = DateTime.UtcNow
                    }).ToList();
                }
            }
            catch (JsonException)
            {
                // Fall through to try legacy format
            }

            // Fallback: AutoML returns JSON like "[0, 1, 0]" - array of class predictions
            var predictions = JsonSerializer.Deserialize<int[]>(responseText);
            if (predictions == null || predictions.Length != appointmentList.Count)
            {
                Console.WriteLine($"ML endpoint returned unexpected format: {responseText}");
                return [];
            }

            // Map predictions back to Prediction objects
            // Class 0 = will attend (low risk), Class 1 = will no-show (high risk)
            return appointmentList.Zip(predictions, (appt, pred) => new Prediction
            {
                AppointmentId = appt.AppointmentId,
                // Binary classification: 1 = predicted no-show, 0 = predicted attend
                PredictedNoShow = pred == 1,
                RiskFactors = GetRiskFactorsForAppointment(appt, pred),
                ModelVersion = _modelVersion,
                PredictedAt = DateTime.UtcNow
            }).ToList();
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"ML endpoint error: {ex.Message}");
            return [];
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"ML response parse error: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Convert appointment to feature row in correct column order.
    /// </summary>
    private static object[] CreateFeatureRow(Appointment appt)
    {
        return
        [
            appt.Patient?.PatientAgeBucket ?? "40-64",
            appt.Patient?.PatientGender ?? "F",
            appt.Patient?.PatientZipCode ?? "00000",
            appt.Patient?.PatientRaceEthnicity ?? "Unknown",
            appt.Patient?.PortalEngaged ?? false,
            (double)(appt.Patient?.HistoricalNoShowRate ?? 0.0m),
            appt.Patient?.HistoricalNoShowCount ?? 0,
            appt.Insurance?.Sipg2 ?? "Commercial",
            appt.LeadTimeDays,
            appt.AppointmentTypeName ?? "E&M EST PCP 3",
            appt.VirtualFlag ?? "Non-Virtual",
            appt.NewPatientFlag ?? "EST PATIENT",
            appt.DayOfWeek,
            appt.HourOfDay,
            appt.AppointmentDuration,
            appt.WebSchedulableYn ?? 0,
            appt.Provider?.ProviderSpecialty ?? "Internal Medicine",
            appt.Provider?.ProviderType ?? "Physician",
            appt.Department?.DepartmentSpecialty ?? "Internal Medicine",
            appt.Department?.PlaceOfServiceType ?? "Office",
            appt.Department?.Market ?? "Madison"
        ];
    }

    /// <summary>
    /// Generate risk factors based on appointment features and prediction.
    /// </summary>
    private static List<RiskFactor> GetRiskFactorsForAppointment(Appointment appt, int prediction)
    {
        var factors = new List<RiskFactor>();

        if (prediction == 1) // Predicted no-show
        {
            // Add risk factors that likely contributed to high-risk prediction
            if ((appt.Patient?.HistoricalNoShowRate ?? 0) > 0.2m)
            {
                factors.Add(new RiskFactor
                {
                    FactorName = "Historical No-Show Rate",
                    Contribution = 0.25m,
                    Direction = "Increases"
                });
            }

            if (appt.LeadTimeDays > 14)
            {
                factors.Add(new RiskFactor
                {
                    FactorName = "Long Lead Time",
                    Contribution = 0.15m,
                    Direction = "Increases"
                });
            }

            if (appt.Insurance?.Sipg2 == "Medicaid")
            {
                factors.Add(new RiskFactor
                {
                    FactorName = "Insurance Type",
                    Contribution = 0.12m,
                    Direction = "Increases"
                });
            }

            if (appt.DayOfWeek == 0) // Monday
            {
                factors.Add(new RiskFactor
                {
                    FactorName = "Monday Appointment",
                    Contribution = 0.08m,
                    Direction = "Increases"
                });
            }

            if (!(appt.Patient?.PortalEngaged ?? false))
            {
                factors.Add(new RiskFactor
                {
                    FactorName = "No Portal Engagement",
                    Contribution = 0.10m,
                    Direction = "Increases"
                });
            }
        }

        return factors.Take(5).ToList();
    }

    #region Request DTOs for MLflow format

    private class MLflowRequest
    {
        [JsonPropertyName("input_data")]
        public DataFrameSplit InputData { get; init; } = new();

        [JsonPropertyName("params")]
        public Dictionary<string, string>? Params { get; init; }
    }

    private class DataFrameSplit
    {
        [JsonPropertyName("columns")]
        public string[] Columns { get; init; } = [];

        [JsonPropertyName("data")]
        public List<object[]> Data { get; init; } = [];
    }

    #endregion

    #region Response DTOs for custom scoring script

    private class MLPredictionResponse
    {
        [JsonPropertyName("predictions")]
        public List<MLPredictionItem>? Predictions { get; init; }
    }

    private class MLPredictionItem
    {
        [JsonPropertyName("no_show_probability")]
        public decimal NoShowProbability { get; init; }

        [JsonPropertyName("risk_level")]
        public string? RiskLevel { get; init; }
    }

    #endregion
}
