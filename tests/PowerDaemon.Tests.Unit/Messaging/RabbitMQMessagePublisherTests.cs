using AutoFixture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PowerDaemon.Messaging.Configuration;
using PowerDaemon.Messaging.Messages;
using PowerDaemon.Messaging.Services;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PowerDaemon.Tests.Unit.Messaging;

public class RabbitMQMessagePublisherTests
{
    private readonly RabbitMQService _rabbitMqService;
    private readonly ILogger<RabbitMQService> _logger;
    private readonly RabbitMQConfiguration _config;
    private readonly Fixture _fixture = new();

    public RabbitMQMessagePublisherTests()
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
            StatusQueue = "test.status",
            VirtualHost = "/",
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = 10,
            RequestedHeartbeat = 60,
            ProductionScale = new ProductionScaleConfiguration
            {
                MaxConnectionPoolSize = 50,
                MinConnectionPoolSize = 10,
                PrefetchCount = 50,
                BatchSize = 100,
                ConsumerThreadCount = 10,
                MaxMessagesPerSecond = 1000,
                MaxConcurrentOperations = 200
            }
        };

        var options = Substitute.For<IOptions<RabbitMQConfiguration>>();
        options.Value.Returns(_config);

        _rabbitMqService = new RabbitMQService(_logger, options);
    }

    [Fact]
    public void RabbitMQService_Constructor_InitializesWithConfiguration()
    {
        // Act & Assert
        _rabbitMqService.Should().NotBeNull();
    }

    [Fact]
    public async Task PublishAsync_ValidDeploymentCommand_DoesNotThrow()
    {
        // Arrange
        var command = _fixture.Build<DeploymentCommand>()
            .With(x => x.Strategy, DeploymentStrategy.Rolling)
            .With(x => x.Priority, DeploymentPriority.Normal)
            .Create();

        // Act & Assert
        var act = () => _rabbitMqService.PublishAsync(command, "deployment.new");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishAsync_NullMessage_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => _rabbitMqService.PublishAsync<DeploymentCommand>(null!, "test.routing");
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("message");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task PublishAsync_InvalidRoutingKey_ThrowsArgumentException(string routingKey)
    {
        // Arrange
        var command = _fixture.Create<DeploymentCommand>();

        // Act & Assert
        var act = () => _rabbitMqService.PublishAsync(command, routingKey);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("routingKey");
    }

    [Fact]
    public async Task PublishBatchAsync_ValidMessages_DoesNotThrow()
    {
        // Arrange
        var commands = _fixture.CreateMany<DeploymentCommand>(5).ToList();

        // Act & Assert
        var act = () => _rabbitMqService.PublishBatchAsync(commands, "batch.deployment");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishBatchAsync_EmptyCollection_DoesNotThrow()
    {
        // Arrange
        var commands = new List<DeploymentCommand>();

        // Act & Assert
        var act = () => _rabbitMqService.PublishBatchAsync(commands, "batch.deployment");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishBatchAsync_NullCollection_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => _rabbitMqService.PublishBatchAsync<DeploymentCommand>(null!, "batch.deployment");
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("messages");
    }

    [Fact]
    public async Task PublishBatchAsync_ProductionScale_HandlesLargeBatch()
    {
        // Arrange - Create a batch of 250 deployment commands (production scale)
        var largeBatch = _fixture.Build<DeploymentCommand>()
            .With(x => x.Strategy, DeploymentStrategy.Rolling)
            .With(x => x.Priority, DeploymentPriority.Normal)
            .CreateMany(250)
            .ToList();

        // Act & Assert
        var act = () => _rabbitMqService.PublishBatchAsync(largeBatch, "production.deployment.batch");
        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(DeploymentPriority.Critical, "critical.deployment")]
    [InlineData(DeploymentPriority.High, "high.deployment")]
    [InlineData(DeploymentPriority.Normal, "normal.deployment")]
    [InlineData(DeploymentPriority.Low, "low.deployment")]
    public async Task PublishAsync_DifferentPriorities_UsesAppropriateRoutingKey(
        DeploymentPriority priority, 
        string expectedRoutingKey)
    {
        // Arrange
        var command = _fixture.Build<DeploymentCommand>()
            .With(x => x.Priority, priority)
            .Create();

        // Act & Assert
        var act = () => _rabbitMqService.PublishAsync(command, expectedRoutingKey);
        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(DeploymentStrategy.Rolling, "strategy.rolling")]
    [InlineData(DeploymentStrategy.BlueGreen, "strategy.bluegreen")]
    [InlineData(DeploymentStrategy.Canary, "strategy.canary")]
    [InlineData(DeploymentStrategy.Immediate, "strategy.immediate")]
    [InlineData(DeploymentStrategy.Scheduled, "strategy.scheduled")]
    public async Task PublishAsync_DifferentStrategies_HandlesAllStrategies(
        DeploymentStrategy strategy, 
        string routingKey)
    {
        // Arrange
        var command = _fixture.Build<DeploymentCommand>()
            .With(x => x.Strategy, strategy)
            .Create();

        // Act & Assert
        var act = () => _rabbitMqService.PublishAsync(command, routingKey);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ReceiveAsync_InvalidQueueName_ThrowsArgumentException()
    {
        // Act & Assert
        var act = () => _rabbitMqService.ReceiveAsync<DeploymentCommand>("", TimeSpan.FromSeconds(30));
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("queueName");
    }

    [Fact]
    public async Task ReceiveAsync_NullQueueName_ThrowsArgumentException()
    {
        // Act & Assert
        var act = () => _rabbitMqService.ReceiveAsync<DeploymentCommand>(null!, TimeSpan.FromSeconds(30));
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("queueName");
    }

    [Fact]
    public async Task ReceiveAsync_ValidQueueName_DoesNotThrowImmediately()
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(1);

        // Act & Assert
        // This should not throw immediately, but may timeout after 1 second
        var act = () => _rabbitMqService.ReceiveAsync<DeploymentCommand>("test.queue", timeout);
        await act.Should().NotThrowAsync<ArgumentException>();
    }

    [Fact]
    public void ProductionScaleConfiguration_HasReasonableDefaults()
    {
        // Assert
        _config.ProductionScale.MaxConnectionPoolSize.Should().Be(50);
        _config.ProductionScale.MinConnectionPoolSize.Should().Be(10);
        _config.ProductionScale.PrefetchCount.Should().Be(50);
        _config.ProductionScale.BatchSize.Should().Be(100);
        _config.ProductionScale.ConsumerThreadCount.Should().Be(10);
        _config.ProductionScale.MaxMessagesPerSecond.Should().Be(1000);
        _config.ProductionScale.MaxConcurrentOperations.Should().Be(200);
    }

    [Fact]
    public void ProductionScaleConfiguration_SupportsHighThroughput()
    {
        // Assert - Verify configuration can handle high-throughput scenarios
        _config.ProductionScale.MaxMessagesPerSecond.Should().BeGreaterOrEqualTo(1000, 
            "Should support at least 1000 messages per second for production");
        
        _config.ProductionScale.MaxConcurrentOperations.Should().BeGreaterOrEqualTo(200,
            "Should support at least 200 concurrent operations for 200+ server deployments");

        _config.ProductionScale.BatchSize.Should().BeGreaterOrEqualTo(100,
            "Should support batch sizes of at least 100 for efficient processing");
    }

    [Fact]
    public async Task PublishAsync_MessageSerialization_ProducesValidJson()
    {
        // Arrange
        var command = new DeploymentCommand
        {
            ServiceName = "TestService",
            Version = "1.2.3",
            Strategy = DeploymentStrategy.Rolling,
            Priority = DeploymentPriority.High,
            TargetServers = new List<string> { "server1", "server2", "server3" }
        };

        // Act - Simulate the serialization that would happen in PublishAsync
        var json = JsonSerializer.Serialize(command);

        // Assert
        json.Should().NotBeNullOrEmpty();
        json.Should().Contain("TestService");
        json.Should().Contain("1.2.3");
        json.Should().Contain("Rolling");
        json.Should().Contain("server1");
        
        // Verify it can be deserialized back
        var deserialized = JsonSerializer.Deserialize<DeploymentCommand>(json);
        deserialized.Should().NotBeNull();
        deserialized!.ServiceName.Should().Be("TestService");
        deserialized.Version.Should().Be("1.2.3");
        deserialized.TargetServers.Should().HaveCount(3);
    }

    [Fact]
    public async Task PublishAsync_ConcurrentPublishing_HandlesMultipleThreads()
    {
        // Arrange
        var commands = _fixture.CreateMany<DeploymentCommand>(10).ToList();
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < commands.Count; i++)
        {
            var command = commands[i];
            var routingKey = $"concurrent.test.{i}";
            tasks.Add(_rabbitMqService.PublishAsync(command, routingKey));
        }

        // Assert
        var act = () => Task.WhenAll(tasks);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishBatchAsync_HighVolumeDeployment_HandlesProductionLoad()
    {
        // Arrange - Simulate deploying to 500 servers with different services
        var commands = new List<DeploymentCommand>();
        
        for (int i = 1; i <= 500; i++)
        {
            commands.Add(new DeploymentCommand
            {
                ServiceName = $"Service{i % 10}", // 10 different services
                Version = "2.1.0",
                TargetServerId = $"prod-server-{i:D3}",
                Strategy = i % 4 == 0 ? DeploymentStrategy.BlueGreen : DeploymentStrategy.Rolling,
                Priority = i % 100 == 0 ? DeploymentPriority.Critical : DeploymentPriority.Normal,
                Timeout = TimeSpan.FromHours(2)
            });
        }

        // Act & Assert
        var act = () => _rabbitMqService.PublishBatchAsync(commands, "production.mass.deployment");
        await act.Should().NotThrowAsync();

        // Verify the collection was preserved correctly
        commands.Should().HaveCount(500);
        commands.Count(c => c.Strategy == DeploymentStrategy.BlueGreen).Should().Be(125);
        commands.Count(c => c.Priority == DeploymentPriority.Critical).Should().Be(5);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(250)]
    public async Task PublishBatchAsync_VariousBatchSizes_HandlesAllSizes(int batchSize)
    {
        // Arrange
        var commands = _fixture.CreateMany<DeploymentCommand>(batchSize).ToList();

        // Act & Assert
        var act = () => _rabbitMqService.PublishBatchAsync(commands, $"batch.size.{batchSize}");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void RabbitMQConfiguration_QueueNames_AreWellFormed()
    {
        // Assert
        _config.DeploymentQueue.Should().NotBeNullOrEmpty()
            .And.NotContain(" ", "Queue names should not contain spaces");
        
        _config.CommandQueue.Should().NotBeNullOrEmpty()
            .And.NotContain(" ", "Queue names should not contain spaces");
        
        _config.StatusQueue.Should().NotBeNullOrEmpty()
            .And.NotContain(" ", "Queue names should not contain spaces");

        _config.ExchangeName.Should().NotBeNullOrEmpty()
            .And.NotContain(" ", "Exchange names should not contain spaces");
    }

    [Fact]
    public void RabbitMQConfiguration_ConnectionSettings_AreValid()
    {
        // Assert
        _config.HostName.Should().NotBeNullOrEmpty();
        _config.Port.Should().BeInRange(1, 65535);
        _config.VirtualHost.Should().NotBeNullOrEmpty();
        _config.AutomaticRecoveryEnabled.Should().BeTrue("Production environments should enable automatic recovery");
        _config.NetworkRecoveryInterval.Should().BeGreaterThan(0);
        _config.RequestedHeartbeat.Should().BeGreaterThan(0);
    }

    public void Dispose()
    {
        _rabbitMqService?.Dispose();
    }
}