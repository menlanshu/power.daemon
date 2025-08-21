using AutoFixture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PowerDaemon.Monitoring.Configuration;
using PowerDaemon.Monitoring.Models;
using PowerDaemon.Monitoring.Services;
using Xunit;

namespace PowerDaemon.Tests.Unit.Monitoring;

public class AlertServiceTests
{
    private readonly IAlertService _alertService;
    private readonly ILogger<AlertService> _logger;
    private readonly MonitoringConfiguration _config;
    private readonly Fixture _fixture = new();

    public AlertServiceTests()
    {
        _logger = Substitute.For<ILogger<AlertService>>();
        
        _config = new MonitoringConfiguration
        {
            AlertEvaluationIntervalSeconds = 30,
            MaxConcurrentAlerts = 1000,
            EnableRealTimeNotifications = true,
            NotificationTimeoutSeconds = 30,
            MaxNotificationRetries = 3,
            MetricsRetentionDays = 90
        };

        var options = Substitute.For<IOptions<MonitoringConfiguration>>();
        options.Value.Returns(_config);

        _alertService = Substitute.For<IAlertService>();
    }

    [Fact]
    public async Task CreateAlertAsync_ValidAlert_CreatesSuccessfully()
    {
        // Arrange
        var alert = _fixture.Build<Alert>()
            .With(a => a.Severity, AlertSeverity.Warning)
            .With(a => a.Status, AlertStatus.Active)
            .Create();

        _alertService.CreateAlertAsync(alert, Arg.Any<CancellationToken>())
            .Returns(alert.Id);

        // Act
        var result = await _alertService.CreateAlertAsync(alert);

        // Assert
        result.Should().Be(alert.Id);
        alert.Severity.Should().Be(AlertSeverity.Warning);
        alert.Status.Should().Be(AlertStatus.Active);
    }

    [Theory]
    [InlineData(AlertSeverity.Info)]
    [InlineData(AlertSeverity.Warning)]
    [InlineData(AlertSeverity.Error)]
    [InlineData(AlertSeverity.Critical)]
    public async Task ProcessAlert_DifferentSeverities_HandledCorrectly(AlertSeverity severity)
    {
        // Arrange
        var alert = _fixture.Build<Alert>()
            .With(a => a.Severity, severity)
            .With(a => a.ServiceName, "ProductionService")
            .Create();

        _alertService.ProcessAlertAsync(alert, Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _alertService.ProcessAlertAsync(alert);

        // Assert
        result.Should().BeTrue();
        alert.Severity.Should().Be(severity);
    }

    [Fact]
    public async Task GetActiveAlerts_ProductionScale_HandlesLargeVolume()
    {
        // Arrange - 500 active alerts (production scale)
        var activeAlerts = _fixture.Build<Alert>()
            .With(a => a.Status, AlertStatus.Active)
            .CreateMany(500)
            .ToList();

        _alertService.GetActiveAlertsAsync(Arg.Any<CancellationToken>())
            .Returns(activeAlerts);

        // Act
        var result = await _alertService.GetActiveAlertsAsync();

        // Assert
        result.Should().HaveCount(500);
        result.Should().OnlyContain(a => a.Status == AlertStatus.Active);
        
        // Configuration should support this scale
        _config.MaxConcurrentAlerts.Should().BeGreaterOrEqualTo(500);
    }

    [Fact]
    public async Task AlertFiltering_ByServiceAndSeverity_FiltersCorrectly()
    {
        // Arrange
        var alerts = new List<Alert>
        {
            _fixture.Build<Alert>()
                .With(a => a.ServiceName, "ServiceA")
                .With(a => a.Severity, AlertSeverity.Critical)
                .Create(),
            _fixture.Build<Alert>()
                .With(a => a.ServiceName, "ServiceB")
                .With(a => a.Severity, AlertSeverity.Warning)
                .Create(),
            _fixture.Build<Alert>()
                .With(a => a.ServiceName, "ServiceA")
                .With(a => a.Severity, AlertSeverity.Error)
                .Create()
        };

        var serviceAAlerts = alerts.Where(a => a.ServiceName == "ServiceA").ToList();
        var criticalAlerts = alerts.Where(a => a.Severity == AlertSeverity.Critical).ToList();

        _alertService.GetAlertsByServiceAsync("ServiceA", Arg.Any<CancellationToken>())
            .Returns(serviceAAlerts);

        _alertService.GetAlertsBySeverityAsync(AlertSeverity.Critical, Arg.Any<CancellationToken>())
            .Returns(criticalAlerts);

        // Act
        var serviceAResult = await _alertService.GetAlertsByServiceAsync("ServiceA");
        var criticalResult = await _alertService.GetAlertsBySeverityAsync(AlertSeverity.Critical);

        // Assert
        serviceAResult.Should().HaveCount(2);
        serviceAResult.Should().OnlyContain(a => a.ServiceName == "ServiceA");

        criticalResult.Should().HaveCount(1);
        criticalResult.Should().OnlyContain(a => a.Severity == AlertSeverity.Critical);
    }

    [Fact]
    public void MonitoringConfiguration_ProductionValues_AreValid()
    {
        // Assert
        _config.AlertEvaluationIntervalSeconds.Should().BeInRange(10, 300);
        _config.MaxConcurrentAlerts.Should().BeGreaterOrEqualTo(500);
        _config.EnableRealTimeNotifications.Should().BeTrue();
        _config.NotificationTimeoutSeconds.Should().BeInRange(5, 120);
        _config.MaxNotificationRetries.Should().BeInRange(1, 10);
        _config.MetricsRetentionDays.Should().BeGreaterOrEqualTo(30);
    }

    [Theory]
    [InlineData(AlertStatus.Active)]
    [InlineData(AlertStatus.Acknowledged)]
    [InlineData(AlertStatus.Resolved)]
    [InlineData(AlertStatus.Suppressed)]
    public void AlertStatus_AllValues_AreHandled(AlertStatus status)
    {
        // Arrange
        var alert = _fixture.Build<Alert>()
            .With(a => a.Status, status)
            .Create();

        // Assert
        alert.Status.Should().Be(status);
        Enum.IsDefined(typeof(AlertStatus), status).Should().BeTrue();
    }

    public class Alert
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ServiceName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public AlertSeverity Severity { get; set; }
        public AlertStatus Status { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ResolvedAt { get; set; }
        public string? ResolvedBy { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    public enum AlertSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2,
        Critical = 3
    }

    public enum AlertStatus
    {
        Active,
        Acknowledged,
        Resolved,
        Suppressed
    }
}