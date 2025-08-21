using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PowerDaemon.Messaging.Configuration;
using PowerDaemon.Messaging.Messages;
using PowerDaemon.Messaging.Services;
using RabbitMQ.Client;
using Xunit;

namespace PowerDaemon.Tests.Unit.Services;

public class RabbitMQServiceTests : IDisposable
{
    private readonly RabbitMQService _rabbitmqService;
    private readonly ILogger<RabbitMQService> _logger;
    private readonly RabbitMQConfiguration _config;

    public RabbitMQServiceTests()
    {
        _logger = Substitute.For<ILogger<RabbitMQService>>();
        _config = new RabbitMQConfiguration
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "test",
            Password = "test",
            ExchangeName = "test-exchange",
            DeploymentQueue = "test.deployments",
            CommandQueue = "test.commands",
            StatusQueue = "test.status"
        };

        var options = Substitute.For<IOptions<RabbitMQConfiguration>>();
        options.Value.Returns(_config);

        _rabbitmqService = new RabbitMQService(_logger, options);
    }

    [Fact]
    public void Constructor_ValidConfiguration_InitializesCorrectly()
    {
        // Act & Assert - Constructor should not throw
        var service = new RabbitMQService(_logger, Options.Create(_config));
        service.Should().NotBeNull();
    }

    [Fact]
    public async Task PublishAsync_ValidMessage_ShouldNotThrow()
    {
        // Arrange
        var deploymentCommand = new DeploymentCommand
        {
            Id = "test-deployment",
            DeploymentId = "deploy-123",
            TargetServerId = "server-1",
            ServiceName = "TestService",
            Version = "1.0.0",
            Strategy = DeploymentStrategy.Rolling
        };

        // Act & Assert
        // Note: This test will verify the method signature and basic validation
        // Actual RabbitMQ connection testing requires integration tests
        var action = () => _rabbitmqService.PublishAsync(deploymentCommand, "test.routing.key");
        await action.Should().NotThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PublishBatchAsync_ValidMessages_ShouldNotThrow()
    {
        // Arrange
        var commands = new List<DeploymentCommand>
        {
            new DeploymentCommand
            {
                Id = "deploy-1",
                DeploymentId = "batch-deploy-1",
                TargetServerId = "server-1",
                ServiceName = "Service1",
                Version = "1.0.0"
            },
            new DeploymentCommand
            {
                Id = "deploy-2",
                DeploymentId = "batch-deploy-2", 
                TargetServerId = "server-2",
                ServiceName = "Service2",
                Version = "1.0.1"
            }
        };

        // Act & Assert
        var action = () => _rabbitmqService.PublishBatchAsync(commands, "test.batch.routing");
        await action.Should().NotThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PublishAsync_NullMessage_ThrowsArgumentNullException()
    {
        // Act & Assert
        var action = () => _rabbitmqService.PublishAsync<DeploymentCommand>(null!, "test.routing");
        await action.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PublishAsync_NullRoutingKey_ThrowsArgumentException()
    {
        // Arrange
        var command = new DeploymentCommand
        {
            Id = "test",
            DeploymentId = "test-deploy",
            ServiceName = "TestService"
        };

        // Act & Assert
        var action = () => _rabbitmqService.PublishAsync(command, null!);
        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PublishAsync_EmptyRoutingKey_ThrowsArgumentException()
    {
        // Arrange
        var command = new DeploymentCommand
        {
            Id = "test",
            DeploymentId = "test-deploy", 
            ServiceName = "TestService"
        };

        // Act & Assert
        var action = () => _rabbitmqService.PublishAsync(command, string.Empty);
        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task ReceiveAsync_InvalidQueueName_ThrowsArgumentException(string queueName)
    {
        // Act & Assert
        var action = () => _rabbitmqService.ReceiveAsync<DeploymentCommand>(queueName, TimeSpan.FromSeconds(30));
        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void Configuration_Properties_AreSetCorrectly()
    {
        // Assert
        _config.HostName.Should().Be("localhost");
        _config.Port.Should().Be(5672);
        _config.UserName.Should().Be("test");
        _config.Password.Should().Be("test");
        _config.ExchangeName.Should().Be("test-exchange");
        _config.DeploymentQueue.Should().Be("test.deployments");
        _config.CommandQueue.Should().Be("test.commands");
        _config.StatusQueue.Should().Be("test.status");
    }

    [Fact]
    public void ProductionScaleSettings_DefaultValues_AreReasonable()
    {
        // Arrange
        var productionConfig = new RabbitMQConfiguration();

        // Assert
        productionConfig.ProductionScale.MaxConnectionPoolSize.Should().Be(50);
        productionConfig.ProductionScale.MinConnectionPoolSize.Should().Be(10);
        productionConfig.ProductionScale.PrefetchCount.Should().Be(50);
        productionConfig.ProductionScale.BatchSize.Should().Be(100);
        productionConfig.ProductionScale.ConsumerThreadCount.Should().Be(10);
        productionConfig.ProductionScale.MaxMessagesPerSecond.Should().Be(1000);
        productionConfig.ProductionScale.MaxConcurrentOperations.Should().Be(200);
    }

    [Fact]
    public void DeploymentCommand_RequiredProperties_AreNotEmpty()
    {
        // Arrange & Act
        var command = new DeploymentCommand
        {
            DeploymentId = "deploy-123",
            TargetServerId = "server-1",
            ServiceName = "TestService",
            Version = "1.0.0",
            Strategy = DeploymentStrategy.Rolling,
            IssuedBy = "user1"
        };

        // Assert
        command.Id.Should().NotBeEmpty();
        command.DeploymentId.Should().Be("deploy-123");
        command.TargetServerId.Should().Be("server-1");
        command.ServiceName.Should().Be("TestService");
        command.Version.Should().Be("1.0.0");
        command.Strategy.Should().Be(DeploymentStrategy.Rolling);
        command.IssuedBy.Should().Be("user1");
        command.Priority.Should().Be(DeploymentPriority.Normal);
        command.IssuedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ServiceCommand_AllCommands_AreDefined()
    {
        // Assert - Verify all expected commands are defined
        PowerDaemon.Messaging.Messages.OrchestratorServiceCommand.Deploy.Should().Be("Deploy");
        PowerDaemon.Messaging.Messages.OrchestratorServiceCommand.Rollback.Should().Be("Rollback");
        PowerDaemon.Messaging.Messages.OrchestratorServiceCommand.Stop.Should().Be("Stop");
        PowerDaemon.Messaging.Messages.OrchestratorServiceCommand.Start.Should().Be("Start");
        PowerDaemon.Messaging.Messages.OrchestratorServiceCommand.Restart.Should().Be("Restart");
        PowerDaemon.Messaging.Messages.OrchestratorServiceCommand.HealthCheck.Should().Be("HealthCheck");
    }

    [Theory]
    [InlineData(DeploymentStrategy.Rolling)]
    [InlineData(DeploymentStrategy.BlueGreen)]
    [InlineData(DeploymentStrategy.Canary)]
    [InlineData(DeploymentStrategy.Immediate)]
    [InlineData(DeploymentStrategy.Scheduled)]
    public void DeploymentStrategy_AllStrategies_AreSupported(DeploymentStrategy strategy)
    {
        // Arrange & Act
        var command = new DeploymentCommand
        {
            Strategy = strategy
        };

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

    public void Dispose()
    {
        _rabbitmqService?.Dispose();
    }
}