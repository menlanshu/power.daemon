using AutoFixture;
using AutoFixture.Xunit2;
using FluentAssertions;
using PowerDaemon.Messaging.Messages;
using System.Text.Json;
using Xunit;

namespace PowerDaemon.Tests.Unit.Messaging;

public class DeploymentCommandTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public void DeploymentCommand_DefaultConstructor_InitializesWithDefaults()
    {
        // Act
        var command = new DeploymentCommand();

        // Assert
        command.Id.Should().NotBeNullOrEmpty();
        command.Priority.Should().Be(DeploymentPriority.Normal);
        command.Strategy.Should().Be(DeploymentStrategy.Rolling);
        command.Timeout.Should().Be(TimeSpan.FromMinutes(30));
        command.IssuedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
        command.Configuration.Should().NotBeNull().And.BeEmpty();
        command.Metadata.Should().NotBeNull().And.BeEmpty();
        command.TargetServers.Should().NotBeNull().And.BeEmpty();
        command.Parameters.Should().NotBeNull().And.BeEmpty();
    }

    [Theory]
    [AutoData]
    public void DeploymentCommand_SetProperties_AllPropertiesRetainValues(
        string deploymentId, 
        string targetServerId, 
        string serviceName, 
        string version,
        string packageUrl,
        string issuedBy,
        string rollbackVersion)
    {
        // Arrange
        var targetServers = new List<string> { "server1", "server2", "server3" };
        var parameters = new Dictionary<string, object> { ["timeout"] = 300, ["retries"] = 3 };
        var configuration = new Dictionary<string, object> { ["poolSize"] = 10, ["debug"] = true };
        var metadata = new Dictionary<string, string> { ["environment"] = "prod", ["team"] = "devops" };

        // Act
        var command = new DeploymentCommand
        {
            DeploymentId = deploymentId,
            TargetServerId = targetServerId,
            ServiceName = serviceName,
            Version = version,
            PackageUrl = packageUrl,
            IssuedBy = issuedBy,
            RollbackVersion = rollbackVersion,
            Strategy = DeploymentStrategy.BlueGreen,
            Priority = DeploymentPriority.High,
            Type = OrchestratorServiceCommand.Deploy,
            TargetServers = targetServers,
            Parameters = parameters,
            Configuration = configuration,
            Metadata = metadata,
            Timeout = TimeSpan.FromHours(2)
        };

        // Assert
        command.DeploymentId.Should().Be(deploymentId);
        command.TargetServerId.Should().Be(targetServerId);
        command.ServiceName.Should().Be(serviceName);
        command.Version.Should().Be(version);
        command.PackageUrl.Should().Be(packageUrl);
        command.IssuedBy.Should().Be(issuedBy);
        command.RollbackVersion.Should().Be(rollbackVersion);
        command.Strategy.Should().Be(DeploymentStrategy.BlueGreen);
        command.Priority.Should().Be(DeploymentPriority.High);
        command.Type.Should().Be(OrchestratorServiceCommand.Deploy);
        command.TargetServers.Should().BeEquivalentTo(targetServers);
        command.Parameters.Should().BeEquivalentTo(parameters);
        command.Configuration.Should().BeEquivalentTo(configuration);
        command.Metadata.Should().BeEquivalentTo(metadata);
        command.Timeout.Should().Be(TimeSpan.FromHours(2));
    }

    [Theory]
    [InlineData(DeploymentStrategy.Rolling)]
    [InlineData(DeploymentStrategy.BlueGreen)]
    [InlineData(DeploymentStrategy.Canary)]
    [InlineData(DeploymentStrategy.Immediate)]
    [InlineData(DeploymentStrategy.Scheduled)]
    public void DeploymentCommand_AllDeploymentStrategies_AreSupported(DeploymentStrategy strategy)
    {
        // Act
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
    public void DeploymentCommand_PriorityValues_HaveCorrectOrder(DeploymentPriority priority, int expectedValue)
    {
        // Act & Assert
        ((int)priority).Should().Be(expectedValue);
    }

    [Fact]
    public void DeploymentCommand_JsonSerialization_RoundTripSucceeds()
    {
        // Arrange
        var originalCommand = _fixture.Build<DeploymentCommand>()
            .With(x => x.Strategy, DeploymentStrategy.Rolling)
            .With(x => x.Priority, DeploymentPriority.High)
            .With(x => x.Type, OrchestratorServiceCommand.Deploy)
            .With(x => x.Timeout, TimeSpan.FromMinutes(45))
            .With(x => x.IssuedAt, DateTime.UtcNow)
            .Create();

        // Act
        var json = JsonSerializer.Serialize(originalCommand);
        var deserializedCommand = JsonSerializer.Deserialize<DeploymentCommand>(json);

        // Assert
        deserializedCommand.Should().NotBeNull();
        deserializedCommand!.Id.Should().Be(originalCommand.Id);
        deserializedCommand.DeploymentId.Should().Be(originalCommand.DeploymentId);
        deserializedCommand.ServiceName.Should().Be(originalCommand.ServiceName);
        deserializedCommand.Version.Should().Be(originalCommand.Version);
        deserializedCommand.Strategy.Should().Be(originalCommand.Strategy);
        deserializedCommand.Priority.Should().Be(originalCommand.Priority);
        deserializedCommand.Type.Should().Be(originalCommand.Type);
        deserializedCommand.IssuedBy.Should().Be(originalCommand.IssuedBy);
        deserializedCommand.Timeout.Should().Be(originalCommand.Timeout);
        deserializedCommand.IssuedAt.Should().BeCloseTo(originalCommand.IssuedAt, TimeSpan.FromSeconds(1));
        deserializedCommand.TargetServers.Should().BeEquivalentTo(originalCommand.TargetServers);
    }

    [Fact]
    public void DeploymentCommand_ProductionScaleScenario_HandlesLargeServerLists()
    {
        // Arrange - Simulate 250 servers
        var targetServers = Enumerable.Range(1, 250)
            .Select(i => $"prod-server-{i:D3}")
            .ToList();

        var largeConfiguration = new Dictionary<string, object>();
        for (int i = 0; i < 50; i++)
        {
            largeConfiguration[$"config_{i}"] = $"value_{i}";
        }

        // Act
        var command = new DeploymentCommand
        {
            ServiceName = "ProductionService",
            Version = "2.1.0",
            Strategy = DeploymentStrategy.Rolling,
            Priority = DeploymentPriority.Critical,
            TargetServers = targetServers,
            Configuration = largeConfiguration,
            Timeout = TimeSpan.FromHours(4) // Longer timeout for large deployments
        };

        // Assert
        command.TargetServers.Should().HaveCount(250);
        command.Configuration.Should().HaveCount(50);
        command.Strategy.Should().Be(DeploymentStrategy.Rolling);
        command.Priority.Should().Be(DeploymentPriority.Critical);
        
        // Verify serialization works with large data
        var json = JsonSerializer.Serialize(command);
        json.Should().NotBeNullOrEmpty();
        
        var deserialized = JsonSerializer.Deserialize<DeploymentCommand>(json);
        deserialized!.TargetServers.Should().HaveCount(250);
        deserialized.Configuration.Should().HaveCount(50);
    }

    [Fact]
    public void DeploymentCommand_CriticalPriorityDeployment_HasExpectedCharacteristics()
    {
        // Act
        var command = new DeploymentCommand
        {
            ServiceName = "CriticalSecurityService",
            Version = "1.2.3-hotfix",
            Priority = DeploymentPriority.Critical,
            Strategy = DeploymentStrategy.Immediate,
            Timeout = TimeSpan.FromMinutes(15), // Short timeout for critical deployments
            IssuedBy = "security-team",
            Type = OrchestratorServiceCommand.Deploy
        };

        // Assert
        command.Priority.Should().Be(DeploymentPriority.Critical);
        command.Strategy.Should().Be(DeploymentStrategy.Immediate);
        ((int)command.Priority).Should().Be(3, "Critical should have highest priority value");
        command.Timeout.Should().BeLessThan(TimeSpan.FromMinutes(30), "Critical deployments should have shorter timeouts");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void DeploymentCommand_WithEmptyServiceName_IsStillValid(string serviceName)
    {
        // Act
        var command = new DeploymentCommand
        {
            ServiceName = serviceName ?? string.Empty
        };

        // Assert - Command should still be constructible
        command.Should().NotBeNull();
        command.ServiceName.Should().Be(serviceName ?? string.Empty);
    }

    [Fact]
    public void DeploymentCommand_MultipleTargetServers_ArePreservedCorrectly()
    {
        // Arrange
        var servers = new List<string>
        {
            "web-01", "web-02", "web-03",
            "api-01", "api-02", "api-03",
            "db-01", "db-02", "cache-01"
        };

        // Act
        var command = new DeploymentCommand
        {
            TargetServers = servers,
            Strategy = DeploymentStrategy.Rolling
        };

        // Assert
        command.TargetServers.Should().HaveCount(9);
        command.TargetServers.Should().Contain("web-01");
        command.TargetServers.Should().Contain("db-02");
        command.TargetServers.Should().ContainInOrder(servers);
    }

    [Fact]
    public void DeploymentCommand_ConfigurationDictionary_SupportsComplexTypes()
    {
        // Arrange
        var complexConfig = new Dictionary<string, object>
        {
            ["simpleString"] = "value",
            ["number"] = 42,
            ["boolean"] = true,
            ["array"] = new[] { "item1", "item2", "item3" },
            ["nested"] = new Dictionary<string, object>
            {
                ["innerKey"] = "innerValue",
                ["innerNumber"] = 100
            }
        };

        // Act
        var command = new DeploymentCommand
        {
            Configuration = complexConfig
        };

        // Assert
        command.Configuration.Should().HaveCount(5);
        command.Configuration["simpleString"].Should().Be("value");
        command.Configuration["number"].Should().Be(42);
        command.Configuration["boolean"].Should().Be(true);
        command.Configuration["array"].Should().BeEquivalentTo(new[] { "item1", "item2", "item3" });
        command.Configuration["nested"].Should().BeOfType<Dictionary<string, object>>();
    }

    [Fact]
    public void DeploymentCommand_TimeoutValidation_AcceptsReasonableValues()
    {
        // Arrange & Act & Assert
        var shortTimeout = new DeploymentCommand { Timeout = TimeSpan.FromMinutes(1) };
        shortTimeout.Timeout.Should().Be(TimeSpan.FromMinutes(1));

        var mediumTimeout = new DeploymentCommand { Timeout = TimeSpan.FromHours(2) };
        mediumTimeout.Timeout.Should().Be(TimeSpan.FromHours(2));

        var longTimeout = new DeploymentCommand { Timeout = TimeSpan.FromHours(12) };
        longTimeout.Timeout.Should().Be(TimeSpan.FromHours(12));
    }

    [Fact]
    public void OrchestratorServiceCommand_AllCommandConstants_AreDefined()
    {
        // Assert - Verify all expected service commands are available
        OrchestratorServiceCommand.Deploy.Should().Be("Deploy");
        OrchestratorServiceCommand.Rollback.Should().Be("Rollback");
        OrchestratorServiceCommand.Stop.Should().Be("Stop");
        OrchestratorServiceCommand.Start.Should().Be("Start");
        OrchestratorServiceCommand.Restart.Should().Be("Restart");
        OrchestratorServiceCommand.HealthCheck.Should().Be("HealthCheck");

        // Verify they're all unique
        var commands = new[]
        {
            OrchestratorServiceCommand.Deploy,
            OrchestratorServiceCommand.Rollback,
            OrchestratorServiceCommand.Stop,
            OrchestratorServiceCommand.Start,
            OrchestratorServiceCommand.Restart,
            OrchestratorServiceCommand.HealthCheck
        };

        commands.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(OrchestratorServiceCommand.Deploy)]
    [InlineData(OrchestratorServiceCommand.Rollback)]
    [InlineData(OrchestratorServiceCommand.Stop)]
    [InlineData(OrchestratorServiceCommand.Start)]
    [InlineData(OrchestratorServiceCommand.Restart)]
    [InlineData(OrchestratorServiceCommand.HealthCheck)]
    public void DeploymentCommand_WithValidCommandTypes_AcceptsAllTypes(string commandType)
    {
        // Act
        var command = new DeploymentCommand { Type = commandType };

        // Assert
        command.Type.Should().Be(commandType);
    }
}