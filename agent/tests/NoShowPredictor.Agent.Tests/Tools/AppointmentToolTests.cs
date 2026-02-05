using FluentAssertions;
using Moq;
using NoShowPredictor.Agent.Data;
using NoShowPredictor.Agent.Models;
using NoShowPredictor.Agent.Tools;
using Xunit;

namespace NoShowPredictor.Agent.Tests.Tools;

/// <summary>
/// Unit tests for AppointmentTool.
/// </summary>
public class AppointmentToolTests
{
    [Theory]
    [InlineData("today")]
    [InlineData("tomorrow")]
    [InlineData("yesterday")]
    [InlineData("this week")]
    [InlineData("next week")]
    [InlineData("next 3 days")]
    [InlineData("2025-06-15")]
    public async Task GetAppointmentsByDateRange_ValidDateExpressions_ReturnsResult(string dateExpression)
    {
        // Arrange
        var mockRepo = new Mock<IAppointmentRepository>();
        mockRepo.Setup(r => r.GetAppointmentsByDateRangeAsync(
            It.IsAny<DateOnly>(),
            It.IsAny<DateOnly>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment>());

        var tool = new AppointmentTool(mockRepo.Object);

        // Act
        var result = await tool.GetAppointmentsByDateRange(dateExpression);

        // Assert
        result.Should().NotBeNull();
        result.DateRange.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetAppointmentsByDateRange_FarFutureDate_ReturnsWarning()
    {
        // Arrange
        var mockRepo = new Mock<IAppointmentRepository>();
        mockRepo.Setup(r => r.GetAppointmentsByDateRangeAsync(
            It.IsAny<DateOnly>(),
            It.IsAny<DateOnly>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment>());

        var tool = new AppointmentTool(mockRepo.Object);
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));

        // Act
        var result = await tool.GetAppointmentsByDateRange(futureDate.ToString("yyyy-MM-dd"));

        // Assert
        result.HasWarnings.Should().BeTrue();
        result.Warnings.Should().Contain(w => w.Contains("less accurate beyond 2 weeks"));
    }

    [Fact]
    public async Task GetAppointmentsByDateRange_WithMissingInsurance_ReturnsDataQualityWarning()
    {
        // Arrange
        var appointmentsWithMissingInsurance = Enumerable.Range(1, 10)
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
                VirtualFlag = "IN PERSON",
                Insurance = i <= 2 ? null : new Insurance { PrimaryPatientInsuranceId = i, PatientId = i } // 20% missing
            })
            .ToList();

        var mockRepo = new Mock<IAppointmentRepository>();
        mockRepo.Setup(r => r.GetAppointmentsByDateRangeAsync(
            It.IsAny<DateOnly>(),
            It.IsAny<DateOnly>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(appointmentsWithMissingInsurance);

        var tool = new AppointmentTool(mockRepo.Object);

        // Act
        var result = await tool.GetAppointmentsByDateRange("tomorrow");

        // Assert
        result.HasWarnings.Should().BeTrue();
        result.Warnings.Should().Contain(w => w.Contains("insurance information"));
    }

    [Fact]
    public async Task GetWeeklyForecast_ReturnsSevenDays()
    {
        // Arrange
        var mockRepo = new Mock<IAppointmentRepository>();
        mockRepo.Setup(r => r.GetAppointmentsByDateRangeAsync(
            It.IsAny<DateOnly>(),
            It.IsAny<DateOnly>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment>
            {
                new Appointment
                {
                    AppointmentId = 1,
                    PatientId = 1,
                    ProviderId = 1,
                    DepartmentId = 1,
                    AppointmentDate = DateOnly.FromDateTime(DateTime.UtcNow),
                    AppointmentStartTime = "09:00",
                    AppointmentDateTime = DateTime.UtcNow.AddHours(9),
                    AppointmentDuration = 30,
                    AppointmentTypeId = 1,
                    AppointmentTypeName = "Follow-up",
                    AppointmentStatus = "Scheduled",
                    AppointmentCreatedDateTime = DateTime.UtcNow.AddDays(-7),
                    AppointmentScheduledDateTime = DateTime.UtcNow.AddDays(-7),
                    NewPatientFlag = "ESTABLISHED",
                    VirtualFlag = "IN PERSON"
                }
            });

        var tool = new AppointmentTool(mockRepo.Object);

        // Act
        var result = await tool.GetWeeklyForecast();

        // Assert
        result.Should().NotBeNull();
        result.Forecasts.Should().HaveCount(7);
        result.Summary.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetPatientNoShowStats_ReturnsCorrectRiskCategory()
    {
        // Arrange
        var mockRepo = new Mock<IAppointmentRepository>();
        mockRepo.Setup(r => r.GetPatientNoShowStatsAsync(
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((10, 6, 0.6m)); // 60% no-show rate

        var tool = new AppointmentTool(mockRepo.Object);

        // Act
        var result = await tool.GetPatientNoShowStats(1);

        // Assert
        result.Should().NotBeNull();
        result.NoShowRate.Should().Be(0.6m);
        result.RiskCategory.Should().Be("High Risk");
    }

    [Theory]
    [InlineData(0.55, "High Risk")]
    [InlineData(0.35, "Moderate Risk")]
    [InlineData(0.15, "Low Risk")]
    public async Task GetPatientNoShowStats_RiskCategory_MatchesRate(decimal rate, string expectedCategory)
    {
        // Arrange
        var mockRepo = new Mock<IAppointmentRepository>();
        mockRepo.Setup(r => r.GetPatientNoShowStatsAsync(
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((10, (int)(10 * rate), rate));

        var tool = new AppointmentTool(mockRepo.Object);

        // Act
        var result = await tool.GetPatientNoShowStats(1);

        // Assert
        result.RiskCategory.Should().Be(expectedCategory);
    }
}
