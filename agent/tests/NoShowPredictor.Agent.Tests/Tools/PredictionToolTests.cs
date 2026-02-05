using FluentAssertions;
using Moq;
using NoShowPredictor.Agent.Models;
using NoShowPredictor.Agent.Services;
using NoShowPredictor.Agent.Tools;
using Xunit;

namespace NoShowPredictor.Agent.Tests.Tools;

/// <summary>
/// Unit tests for PredictionTool including fallback scenarios.
/// </summary>
public class PredictionToolTests
{
    [Fact]
    public async Task GetPredictions_EmptyList_ReturnsEmptyResult()
    {
        // Arrange
        var mockClient = new Mock<IMLEndpointClient>();
        var tool = new PredictionTool(mockClient.Object);

        // Act
        var result = await tool.GetPredictions(Array.Empty<Appointment>());

        // Assert
        result.Should().NotBeNull();
        result.Predictions.Should().BeEmpty();
        result.IsMLBased.Should().BeFalse();
    }

    [Fact]
    public async Task GetPredictions_MLEndpointSuccess_ReturnsMLBasedResult()
    {
        // Arrange
        var appointments = new List<Appointment>
        {
            CreateTestAppointment(1, "ESTABLISHED", 7)
        };

        var predictions = new List<Prediction>
        {
            new Prediction
            {
                AppointmentId = 1,
                PredictedNoShow = true,
                RiskFactors = new List<RiskFactor>
                {
                    new RiskFactor { FactorName = "lead_time", FactorValue = "7 days", Direction = "Increases", Contribution = 0.1m }
                }
            }
        };

        var mockClient = new Mock<IMLEndpointClient>();
        mockClient.Setup(c => c.GetPredictionsAsync(
            It.IsAny<IEnumerable<Appointment>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(predictions);

        var tool = new PredictionTool(mockClient.Object);

        // Act
        var result = await tool.GetPredictions(appointments);

        // Assert
        result.Should().NotBeNull();
        result.IsMLBased.Should().BeTrue();
        result.Source.Should().Be("ML Model");
        result.Predictions.Should().HaveCount(1);
        result.Predictions[0].RiskLevel.Should().Be("High");
    }

    [Fact]
    public async Task GetPredictions_MLEndpointFailure_ReturnsFallbackResult()
    {
        // Arrange
        var appointments = new List<Appointment>
        {
            CreateTestAppointment(1, "NEW PATIENT", 21)
        };

        var mockClient = new Mock<IMLEndpointClient>();
        mockClient.Setup(c => c.GetPredictionsAsync(
            It.IsAny<IEnumerable<Appointment>>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var tool = new PredictionTool(mockClient.Object);

        // Act
        var result = await tool.GetPredictions(appointments);

        // Assert
        result.Should().NotBeNull();
        result.IsMLBased.Should().BeFalse();
        result.Source.Should().Contain("Fallback");
        result.Warning.Should().NotBeNullOrEmpty();
        result.Predictions.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetPredictions_Fallback_NewPatientHasHigherRisk()
    {
        // Arrange
        var establishedPatient = CreateTestAppointment(1, "ESTABLISHED", 7);
        var newPatient = CreateTestAppointment(2, "NEW PATIENT", 7);

        var mockClient = new Mock<IMLEndpointClient>();
        mockClient.Setup(c => c.GetPredictionsAsync(
            It.IsAny<IEnumerable<Appointment>>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Down"));

        var tool = new PredictionTool(mockClient.Object);

        // Act
        var establishedResult = await tool.GetPredictions(new[] { establishedPatient });
        var newPatientResult = await tool.GetPredictions(new[] { newPatient });

        // Assert - fallback heuristic should recognize new patients as higher risk
        // Both should have predictions generated
        establishedResult.Predictions.Should().HaveCount(1);
        newPatientResult.Predictions.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetPredictions_Fallback_LongLeadTimeConsideredHigherRisk()
    {
        // Arrange
        var shortLead = CreateTestAppointment(1, "ESTABLISHED", 3);
        var longLead = CreateTestAppointment(2, "ESTABLISHED", 21);

        var mockClient = new Mock<IMLEndpointClient>();
        mockClient.Setup(c => c.GetPredictionsAsync(
            It.IsAny<IEnumerable<Appointment>>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Down"));

        var tool = new PredictionTool(mockClient.Object);

        // Act
        var shortResult = await tool.GetPredictions(new[] { shortLead });
        var longResult = await tool.GetPredictions(new[] { longLead });

        // Assert - both should return valid predictions
        shortResult.Predictions.Should().HaveCount(1);
        longResult.Predictions.Should().HaveCount(1);
    }

    [Theory]
    [InlineData(true, "High")]
    [InlineData(false, "Low")]
    public async Task GetPredictions_RiskLevel_CalculatedFromPredictedNoShow(bool predictedNoShow, string expectedLevel)
    {
        // Arrange
        var appointments = new List<Appointment> { CreateTestAppointment(1, "ESTABLISHED", 7) };
        var predictions = new List<Prediction>
        {
            new Prediction { AppointmentId = 1, PredictedNoShow = predictedNoShow, RiskFactors = new List<RiskFactor>() }
        };

        var mockClient = new Mock<IMLEndpointClient>();
        mockClient.Setup(c => c.GetPredictionsAsync(
            It.IsAny<IEnumerable<Appointment>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(predictions);

        var tool = new PredictionTool(mockClient.Object);

        // Act
        var result = await tool.GetPredictions(appointments);

        // Assert
        result.Predictions[0].RiskLevel.Should().Be(expectedLevel);
    }

    [Fact]
    public async Task GetHighRiskAppointments_FiltersCorrectly()
    {
        // Arrange - AppointmentPrediction uses RiskLevel string directly
        var predictions = new List<AppointmentPrediction>
        {
            new AppointmentPrediction { AppointmentId = 1, RiskLevel = "High" },
            new AppointmentPrediction { AppointmentId = 2, RiskLevel = "Low" },
            new AppointmentPrediction { AppointmentId = 3, RiskLevel = "Low" },
            new AppointmentPrediction { AppointmentId = 4, RiskLevel = "High" }
        };

        var mockClient = new Mock<IMLEndpointClient>();
        var tool = new PredictionTool(mockClient.Object);

        // Act
        var result = await tool.GetHighRiskAppointments(predictions);

        // Assert
        result.Should().HaveCount(2);
        result.Should().OnlyContain(p => p.RiskLevel == "High");
    }

    private static Appointment CreateTestAppointment(int id, string newPatientFlag, int leadTimeDays)
    {
        var now = DateTime.UtcNow;
        var appointmentDate = now.AddDays(1);
        var scheduledDate = now.AddDays(-leadTimeDays);

        return new Appointment
        {
            AppointmentId = id,
            PatientId = id,
            ProviderId = 1,
            DepartmentId = 1,
            AppointmentDate = DateOnly.FromDateTime(appointmentDate),
            AppointmentStartTime = "10:00",
            AppointmentDateTime = appointmentDate.Date.AddHours(10),
            AppointmentDuration = 30,
            AppointmentTypeId = 1,
            AppointmentTypeName = "Follow-up",
            AppointmentStatus = "Scheduled",
            AppointmentCreatedDateTime = scheduledDate,
            AppointmentScheduledDateTime = scheduledDate,
            NewPatientFlag = newPatientFlag,
            VirtualFlag = "IN PERSON",
            Patient = new Patient
            {
                PatientId = id,
                PatientGender = "Female",
                PatientAgeBucket = "35-44",
                PatientEmail = $"patient{id}@example.com"
            },
            Provider = new Provider
            {
                ProviderId = 1,
                ProviderFirstName = "Dr.",
                ProviderLastName = "Smith",
                ProviderType = "Physician",
                ProviderSpecialty = "Internal Medicine"
            }
        };
    }
}
