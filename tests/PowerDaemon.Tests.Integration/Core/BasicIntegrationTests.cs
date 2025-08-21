using FluentAssertions;
using PowerDaemon.Messaging.Messages;
using PowerDaemon.Messaging.Configuration;
using Xunit;

namespace PowerDaemon.Tests.Integration.Core;

[Trait("Category", "Integration")]
public class BasicIntegrationTests
{
    [Fact]
    public void DeploymentCommand_Serialization_WorksCorrectly()
    {
        // Arrange
        var command = new DeploymentCommand
        {
            DeploymentId = "integration-deploy-123",
            TargetServerId = "integration-server-1",
            ServiceName = "IntegrationTestService",
            Version = "1.0.0",
            Strategy = DeploymentStrategy.Rolling,
            Priority = DeploymentPriority.High,
            IssuedBy = "integration-test",
            Type = OrchestratorServiceCommand.Deploy,
            TargetServers = new List<string> { "server-1", "server-2", "server-3" },
            Parameters = new Dictionary<string, object>
            {
                ["timeout"] = 600,
                ["environment"] = "integration",
                ["healthCheckUrl"] = "http://server:8080/health"
            }
        };

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(command);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<DeploymentCommand>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.DeploymentId.Should().Be("integration-deploy-123");
        deserialized.ServiceName.Should().Be("IntegrationTestService");
        deserialized.Strategy.Should().Be(DeploymentStrategy.Rolling);
        deserialized.TargetServers.Should().HaveCount(3);
        deserialized.Parameters.Should().ContainKey("timeout");
        deserialized.Parameters.Should().ContainKey("environment");
        deserialized.Parameters.Should().ContainKey("healthCheckUrl");
    }

    [Fact]
    public void RabbitMQConfiguration_ValidationRules_Work()
    {
        // Arrange
        var config = new RabbitMQConfiguration
        {
            HostName = "rabbitmq.company.com",
            Port = 5672,
            UserName = "powerdaemon",
            Password = "secret123",
            ExchangeName = "powerdaemon.exchange",
            DeploymentQueue = "powerdaemon.deployments",
            CommandQueue = "powerdaemon.commands",
            StatusQueue = "powerdaemon.status",
            VirtualHost = "/powerdaemon",
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = 10,
            RequestedHeartbeat = 60
        };

        // Act & Assert
        config.HostName.Should().NotBeNullOrEmpty();
        config.Port.Should().BeGreaterThan(0).And.BeLessThan(65536);
        config.ExchangeName.Should().NotBeNullOrEmpty();
        config.DeploymentQueue.Should().NotBeNullOrEmpty();
        config.CommandQueue.Should().NotBeNullOrEmpty();
        config.StatusQueue.Should().NotBeNullOrEmpty();
        config.AutomaticRecoveryEnabled.Should().BeTrue();
        config.NetworkRecoveryInterval.Should().BeGreaterThan(0);
        config.RequestedHeartbeat.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ProductionScaleSettings_RabbitMQ_AreReasonable()
    {
        // Arrange
        var config = new RabbitMQConfiguration();

        // Assert - Check production scale defaults for 200+ server deployment
        config.ProductionScale.MaxConnectionPoolSize.Should().Be(50);
        config.ProductionScale.MinConnectionPoolSize.Should().Be(10);
        config.ProductionScale.PrefetchCount.Should().Be(50);
        config.ProductionScale.BatchSize.Should().Be(100);
        config.ProductionScale.ConsumerThreadCount.Should().Be(10);
        config.ProductionScale.MaxMessagesPerSecond.Should().Be(1000);
        config.ProductionScale.MaxConcurrentOperations.Should().Be(200);
        
        // These settings should handle 200+ servers effectively
        config.ProductionScale.MaxConcurrentOperations.Should().BeGreaterThanOrEqualTo(200);
    }

    [Theory]
    [InlineData("deploy-test-1", "TestService", "1.0.0", DeploymentStrategy.Rolling)]
    [InlineData("deploy-test-2", "WebService", "2.1.0", DeploymentStrategy.BlueGreen)]
    [InlineData("deploy-test-3", "ApiService", "3.0.0", DeploymentStrategy.Canary)]
    public void DeploymentCommand_VariousConfigurations_CreateSuccessfully(
        string deploymentId, string serviceName, string version, DeploymentStrategy strategy)
    {
        // Act
        var command = new DeploymentCommand
        {
            DeploymentId = deploymentId,
            ServiceName = serviceName,
            Version = version,
            Strategy = strategy,
            TargetServerId = "test-server",
            IssuedBy = "integration-test"
        };

        // Assert
        command.DeploymentId.Should().Be(deploymentId);
        command.ServiceName.Should().Be(serviceName);
        command.Version.Should().Be(version);
        command.Strategy.Should().Be(strategy);
        command.Id.Should().NotBeEmpty();
        command.IssuedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void DeploymentCommand_LargeConfiguration_HandlesCorrectly()
    {
        // Arrange - Create a large configuration for stress testing
        var largeConfig = new Dictionary<string, object>();
        var largeMetadata = new Dictionary<string, string>();
        
        for (int i = 0; i < 50; i++)
        {
            largeConfig[$"config_{i}"] = $"value_{i}";
            largeMetadata[$"metadata_{i}"] = $"meta_value_{i}";
        }

        // Act
        var command = new DeploymentCommand
        {
            DeploymentId = "large-config-test",
            ServiceName = "LargeConfigService",
            Version = "1.0.0",
            Configuration = largeConfig,
            Metadata = largeMetadata,
            TargetServers = Enumerable.Range(1, 20).Select(i => $"server-{i}").ToList()
        };

        // Assert
        command.Configuration.Should().HaveCount(50);
        command.Metadata.Should().HaveCount(50);
        command.TargetServers.Should().HaveCount(20);
        command.TargetServers.Should().Contain("server-1");
        command.TargetServers.Should().Contain("server-20");
    }

    [Fact]
    public void ServiceCommand_Constants_AreCorrect()
    {
        // Assert
        OrchestratorServiceCommand.Deploy.Should().Be("Deploy");
        OrchestratorServiceCommand.Rollback.Should().Be("Rollback");
        OrchestratorServiceCommand.Stop.Should().Be("Stop");
        OrchestratorServiceCommand.Start.Should().Be("Start");
        OrchestratorServiceCommand.Restart.Should().Be("Restart");
        OrchestratorServiceCommand.HealthCheck.Should().Be("HealthCheck");
    }
}