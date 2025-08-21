using FluentAssertions;
using PowerDaemon.Messaging.Messages;
using PowerDaemon.Messaging.Configuration;
using Xunit;

namespace PowerDaemon.Tests.Unit.Services;

public class SimpleMessagingTests
{
    [Fact]
    public void DeploymentCommand_DefaultConstructor_InitializesCorrectly()
    {
        // Act
        var command = new DeploymentCommand();

        // Assert
        command.Should().NotBeNull();
        command.Id.Should().NotBeEmpty();
        command.IssuedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        command.Priority.Should().Be(DeploymentPriority.Normal);
        command.Configuration.Should().NotBeNull();
        command.Metadata.Should().NotBeNull();
        command.TargetServers.Should().NotBeNull();
        command.Parameters.Should().NotBeNull();
    }

    [Fact]
    public void DeploymentCommand_WithValues_SetsPropertiesCorrectly()
    {
        // Arrange & Act
        var command = new DeploymentCommand
        {
            DeploymentId = "deploy-123",
            TargetServerId = "server-1",
            ServiceName = "TestService",
            Version = "1.0.0",
            Strategy = DeploymentStrategy.Rolling,
            Priority = DeploymentPriority.High,
            IssuedBy = "user1",
            Type = OrchestratorServiceCommand.Deploy,
            TargetServers = new List<string> { "server-1", "server-2" },
            Parameters = new Dictionary<string, object> { ["timeout"] = 300 }
        };

        // Assert
        command.DeploymentId.Should().Be("deploy-123");
        command.TargetServerId.Should().Be("server-1");
        command.ServiceName.Should().Be("TestService");
        command.Version.Should().Be("1.0.0");
        command.Strategy.Should().Be(DeploymentStrategy.Rolling);
        command.Priority.Should().Be(DeploymentPriority.High);
        command.IssuedBy.Should().Be("user1");
        command.Type.Should().Be(OrchestratorServiceCommand.Deploy);
        command.TargetServers.Should().Contain("server-1");
        command.TargetServers.Should().Contain("server-2");
        command.Parameters.Should().ContainKey("timeout");
    }

    [Theory]
    [InlineData(DeploymentStrategy.Rolling)]
    [InlineData(DeploymentStrategy.BlueGreen)]
    [InlineData(DeploymentStrategy.Canary)]
    [InlineData(DeploymentStrategy.Immediate)]
    [InlineData(DeploymentStrategy.Scheduled)]
    public void DeploymentStrategy_AllValues_AreSupported(DeploymentStrategy strategy)
    {
        // Arrange & Act
        var command = new DeploymentCommand { Strategy = strategy };

        // Assert
        command.Strategy.Should().Be(strategy);
        Enum.IsDefined(typeof(DeploymentStrategy), strategy).Should().BeTrue();
    }

    [Theory]
    [InlineData(DeploymentPriority.Low, 0)]
    [InlineData(DeploymentPriority.Normal, 1)]
    [InlineData(DeploymentPriority.High, 2)]
    [InlineData(DeploymentPriority.Critical, 3)]
    public void DeploymentPriority_EnumValues_AreCorrect(DeploymentPriority priority, int expectedValue)
    {
        // Assert
        ((int)priority).Should().Be(expectedValue);
    }

    [Fact]
    public void OrchestratorServiceCommand_AllCommands_AreDefined()
    {
        // Assert
        OrchestratorServiceCommand.Deploy.Should().Be("Deploy");
        OrchestratorServiceCommand.Rollback.Should().Be("Rollback");
        OrchestratorServiceCommand.Stop.Should().Be("Stop");
        OrchestratorServiceCommand.Start.Should().Be("Start");
        OrchestratorServiceCommand.Restart.Should().Be("Restart");
        OrchestratorServiceCommand.HealthCheck.Should().Be("HealthCheck");
    }

    [Fact]
    public void RabbitMQConfiguration_DefaultValues_AreReasonable()
    {
        // Arrange
        var config = new RabbitMQConfiguration();

        // Assert
        config.HostName.Should().Be("localhost");
        config.Port.Should().Be(5672);
        config.VirtualHost.Should().Be("/");
        config.AutomaticRecoveryEnabled.Should().BeTrue();
        config.NetworkRecoveryInterval.Should().Be(10);
        config.RequestedHeartbeat.Should().Be(60);
    }

    [Fact]
    public void RabbitMQConfiguration_ProductionScale_HasCorrectDefaults()
    {
        // Arrange
        var config = new RabbitMQConfiguration();

        // Assert
        config.ProductionScale.MaxConnectionPoolSize.Should().Be(50);
        config.ProductionScale.MinConnectionPoolSize.Should().Be(10);
        config.ProductionScale.PrefetchCount.Should().Be(50);
        config.ProductionScale.BatchSize.Should().Be(100);
        config.ProductionScale.ConsumerThreadCount.Should().Be(10);
        config.ProductionScale.MaxMessagesPerSecond.Should().Be(1000);
        config.ProductionScale.MaxConcurrentOperations.Should().Be(200);
    }
}