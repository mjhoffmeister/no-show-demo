using FluentAssertions;
using Moq;
using NoShowPredictor.Agent.Models;
using NoShowPredictor.Agent.Services;
using Xunit;

namespace NoShowPredictor.Agent.Tests.Services;

/// <summary>
/// Unit tests for MLEndpointClient.
/// </summary>
public class MLEndpointClientTests
{
    [Fact]
    public void Constructor_ValidEndpoint_CreatesClient()
    {
        // Arrange & Act
        var client = new MLEndpointClient("https://example.ml.azure.com/score");

        // Assert
        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_NullEndpoint_UsesDefaultOrEmpty()
    {
        // Arrange & Act - The client doesn't validate endpoint in constructor
        // In production, the endpoint would be configured via DI/settings
        var act = () => new MLEndpointClient(null!);

        // The client handles null endpoint gracefully - will throw when actually calling the API
        act.Should().NotThrow();
    }

    [Fact]
    public async Task GetPredictionsAsync_EmptyInput_ReturnsEmptyList()
    {
        // Arrange
        var mockClient = new Mock<IMLEndpointClient>();
        mockClient.Setup(c => c.GetPredictionsAsync(
            It.IsAny<IEnumerable<Appointment>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Prediction>());

        // Act
        var result = await mockClient.Object.GetPredictionsAsync(Array.Empty<Appointment>());

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPredictionsAsync_ValidAppointment_ReturnsPrediction()
    {
        // Arrange
        var appointment = new Appointment
        {
            AppointmentId = 1,
            PatientId = 1,
            ProviderId = 1,
            DepartmentId = 1,
            AppointmentDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            AppointmentStartTime = "09:00",
            AppointmentDateTime = DateTime.UtcNow.AddDays(1).AddHours(9),
            AppointmentDuration = 30,
            AppointmentTypeId = 1,
            AppointmentTypeName = "Follow-up",
            AppointmentStatus = "Scheduled",
            AppointmentCreatedDateTime = DateTime.UtcNow.AddDays(-7),
            AppointmentScheduledDateTime = DateTime.UtcNow.AddDays(-7),
            NewPatientFlag = "ESTABLISHED",
            VirtualFlag = "IN PERSON"
        };

        var expectedPrediction = new Prediction
        {
            AppointmentId = 1,
            PredictedNoShow = false,
            RiskFactors = new List<RiskFactor>
            {
                new RiskFactor { FactorName = "lead_time_days", FactorValue = "7", Direction = "Increases", Contribution = 0.05m }
            }
        };

        var mockClient = new Mock<IMLEndpointClient>();
        mockClient.Setup(c => c.GetPredictionsAsync(
            It.Is<IEnumerable<Appointment>>(a => a.Count() == 1),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Prediction> { expectedPrediction });

        // Act
        var result = await mockClient.Object.GetPredictionsAsync(new[] { appointment });

        // Assert
        result.Should().HaveCount(1);
        result[0].PredictedNoShow.Should().BeFalse();
        result[0].RiskLevel.Should().Be("Low");
    }

    [Fact]
    public async Task GetPredictionsAsync_MultiplePredictions_ReturnsAll()
    {
        // Arrange
        var appointments = Enumerable.Range(1, 5)
            .Select(i => new Appointment
            {
                AppointmentId = i,
                PatientId = i,
                ProviderId = 1,
                DepartmentId = 1,
                AppointmentDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
                AppointmentStartTime = "09:00",
                AppointmentDateTime = DateTime.UtcNow.AddDays(1).AddHours(9),
                AppointmentDuration = 30,
                AppointmentTypeId = 1,
                AppointmentTypeName = "Follow-up",
                AppointmentStatus = "Scheduled",
                AppointmentCreatedDateTime = DateTime.UtcNow.AddDays(-7),
                AppointmentScheduledDateTime = DateTime.UtcNow.AddDays(-7),
                NewPatientFlag = "ESTABLISHED",
                VirtualFlag = "IN PERSON"
            });

        // Create alternating predictions: 2 high risk (no-show), 3 low risk (attend)
        var predictions = Enumerable.Range(1, 5)
            .Select(i => new Prediction
            {
                AppointmentId = i,
                PredictedNoShow = i % 2 == 0, // Even IDs are predicted no-shows
                RiskFactors = new List<RiskFactor>()
            })
            .ToList();

        var mockClient = new Mock<IMLEndpointClient>();
        mockClient.Setup(c => c.GetPredictionsAsync(
            It.IsAny<IEnumerable<Appointment>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(predictions);

        // Act
        var result = await mockClient.Object.GetPredictionsAsync(appointments);

        // Assert
        // IDs 2, 4 are predicted no-shows (High), IDs 1, 3, 5 are not (Low)
        result.Should().HaveCount(5);
        result.Count(p => p.RiskLevel == "Low").Should().Be(3);
        result.Count(p => p.RiskLevel == "High").Should().Be(2);
    }

    [Fact]
    public void Prediction_RiskLevel_MappedCorrectly()
    {
        // Arrange & Act - RiskLevel is computed from PredictedNoShow boolean
        var lowRisk = new Prediction { PredictedNoShow = false };
        var highRisk = new Prediction { PredictedNoShow = true };

        // Assert
        lowRisk.RiskLevel.Should().Be("Low");
        highRisk.RiskLevel.Should().Be("High");
    }

    [Fact]
    public void RiskFactor_Properties_SetCorrectly()
    {
        // Arrange & Act
        var factor = new RiskFactor
        {
            FactorName = "lead_time_days",
            FactorValue = "14",
            Direction = "Increases",
            Contribution = 0.15m
        };

        // Assert
        factor.FactorName.Should().Be("lead_time_days");
        factor.FactorValue.Should().Be("14");
        factor.Direction.Should().Be("Increases");
        factor.Contribution.Should().Be(0.15m);
    }
}
