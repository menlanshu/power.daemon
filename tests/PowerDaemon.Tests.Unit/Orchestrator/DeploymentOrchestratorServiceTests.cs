using AutoFixture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PowerDaemon.Cache.Services;
using PowerDaemon.Messaging.Messages;
using PowerDaemon.Messaging.Services;
using PowerDaemon.Orchestrator.Configuration;
using PowerDaemon.Orchestrator.Models;
using PowerDaemon.Orchestrator.Services;
using Xunit;

namespace PowerDaemon.Tests.Unit.Orchestrator;

public class DeploymentOrchestratorServiceTests : IDisposable
{
    private readonly DeploymentOrchestratorService _orchestratorService;
    private readonly ILogger<DeploymentOrchestratorService> _logger;
    private readonly IOptions<OrchestratorConfiguration> _config;
    private readonly IWorkflowRepository _workflowRepository;
    private readonly IWorkflowExecutor _workflowExecutor;
    private readonly IMessagePublisher _messagePublisher;
    private readonly ICacheService _cacheService;
    private readonly List<IDeploymentStrategy> _strategies;
    private readonly Fixture _fixture = new();

    public DeploymentOrchestratorServiceTests()
    {
        _logger = Substitute.For<ILogger<DeploymentOrchestratorService>>();
        _workflowRepository = Substitute.For<IWorkflowRepository>();
        _workflowExecutor = Substitute.For<IWorkflowExecutor>();
        _messagePublisher = Substitute.For<IMessagePublisher>();
        _cacheService = Substitute.For<ICacheService>();

        var orchestratorConfig = new OrchestratorConfiguration
        {
            MaxConcurrentWorkflows = 50,
            MaxQueuedWorkflows = 200,
            HealthCheckIntervalSeconds = 30,
            WorkflowTimeoutMinutes = 120,
            EnableAutoRollback = true,
            RollbackTimeoutMinutes = 60
        };

        _config = Substitute.For<IOptions<OrchestratorConfiguration>>();
        _config.Value.Returns(orchestratorConfig);

        // Create mock deployment strategies
        _strategies = new List<IDeploymentStrategy>
        {
            CreateMockStrategy(DeploymentStrategy.Rolling),
            CreateMockStrategy(DeploymentStrategy.BlueGreen),
            CreateMockStrategy(DeploymentStrategy.Canary),
            CreateMockStrategy(DeploymentStrategy.Immediate)
        };

        _orchestratorService = new DeploymentOrchestratorService(
            _logger,
            _config,
            _workflowRepository,
            _workflowExecutor,
            _messagePublisher,
            _cacheService,
            _strategies
        );
    }

    [Fact]
    public void DeploymentOrchestratorService_Constructor_InitializesCorrectly()
    {
        // Assert
        _orchestratorService.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateWorkflowAsync_ValidRequest_CreatesWorkflow()
    {
        // Arrange
        var request = _fixture.Build<DeploymentWorkflowRequest>()
            .With(x => x.Strategy, DeploymentStrategy.Rolling)
            .With(x => x.TargetServers, new List<string> { "server1", "server2", "server3" })
            .Create();

        var workflowId = Guid.NewGuid().ToString();
        _workflowRepository.CreateWorkflowAsync(Arg.Any<DeploymentWorkflow>(), Arg.Any<CancellationToken>())
            .Returns(workflowId);

        // Act
        var result = await _orchestratorService.CreateWorkflowAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(workflowId);
        result.Strategy.Should().Be(DeploymentStrategy.Rolling);
        result.TargetServers.Should().BeEquivalentTo(request.TargetServers);
        result.ServiceName.Should().Be(request.ServiceName);
        result.Status.Should().Be(WorkflowStatus.Created);

        await _workflowRepository.Received(1).CreateWorkflowAsync(
            Arg.Is<DeploymentWorkflow>(w => w.Strategy == DeploymentStrategy.Rolling), 
            Arg.Any<CancellationToken>());

        await _cacheService.Received(1).SetAsync(
            $"workflow:{workflowId}", 
            Arg.Any<DeploymentWorkflow>(), 
            TimeSpan.FromHours(24));
    }

    [Fact]
    public async Task CreateWorkflowAsync_UnsupportedStrategy_ThrowsArgumentException()
    {
        // Arrange
        var request = _fixture.Build<DeploymentWorkflowRequest>()
            .With(x => x.Strategy, DeploymentStrategy.Scheduled)
            .Create();

        // Act & Assert
        var act = () => _orchestratorService.CreateWorkflowAsync(request);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Unsupported deployment strategy*");
    }

    [Fact]
    public async Task CreateWorkflowAsync_InvalidConfiguration_ThrowsArgumentException()
    {
        // Arrange
        var request = _fixture.Build<DeploymentWorkflowRequest>()
            .With(x => x.Strategy, DeploymentStrategy.Rolling)
            .Create();

        var strategy = _strategies.First(s => s.StrategyType == DeploymentStrategy.Rolling);
        strategy.ValidateConfigurationAsync(Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(false);

        // Act & Assert
        var act = () => _orchestratorService.CreateWorkflowAsync(request);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Invalid deployment configuration*");
    }

    [Fact]
    public async Task StartWorkflowAsync_ValidWorkflow_StartsExecution()
    {
        // Arrange
        var workflowId = Guid.NewGuid().ToString();
        var workflow = _fixture.Build<DeploymentWorkflow>()
            .With(x => x.Id, workflowId)
            .With(x => x.Status, WorkflowStatus.Created)
            .Create();

        _workflowRepository.GetWorkflowAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(workflow);

        _cacheService.AcquireLockAsync(Arg.Any<string>(), Arg.Any<TimeSpan>())
            .Returns(Substitute.For<IDisposable>());

        _workflowExecutor.ExecuteWorkflowAsync(Arg.Any<DeploymentWorkflow>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _orchestratorService.StartWorkflowAsync(workflowId);

        // Assert
        result.Should().BeTrue();
        
        await _workflowRepository.Received().UpdateWorkflowAsync(
            Arg.Is<DeploymentWorkflow>(w => w.Status == WorkflowStatus.Running),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartWorkflowAsync_NonExistentWorkflow_ReturnsFalse()
    {
        // Arrange
        var workflowId = Guid.NewGuid().ToString();
        _workflowRepository.GetWorkflowAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns((DeploymentWorkflow?)null);

        // Act
        var result = await _orchestratorService.StartWorkflowAsync(workflowId);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(WorkflowStatus.Running)]
    [InlineData(WorkflowStatus.Completed)]
    [InlineData(WorkflowStatus.Failed)]
    public async Task StartWorkflowAsync_InvalidWorkflowStatus_ReturnsFalse(WorkflowStatus status)
    {
        // Arrange
        var workflowId = Guid.NewGuid().ToString();
        var workflow = _fixture.Build<DeploymentWorkflow>()
            .With(x => x.Id, workflowId)
            .With(x => x.Status, status)
            .Create();

        _workflowRepository.GetWorkflowAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(workflow);

        // Act
        var result = await _orchestratorService.StartWorkflowAsync(workflowId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task CancelWorkflowAsync_RunningWorkflow_CancelsSuccessfully()
    {
        // Arrange
        var workflowId = Guid.NewGuid().ToString();
        var workflow = _fixture.Build<DeploymentWorkflow>()
            .With(x => x.Id, workflowId)
            .With(x => x.Status, WorkflowStatus.Created)
            .Create();

        _workflowRepository.GetWorkflowAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(workflow);

        _cacheService.AcquireLockAsync(Arg.Any<string>(), Arg.Any<TimeSpan>())
            .Returns(Substitute.For<IDisposable>());

        // First start the workflow
        await _orchestratorService.StartWorkflowAsync(workflowId);

        // Act
        var result = await _orchestratorService.CancelWorkflowAsync(workflowId, "Test cancellation");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task RollbackWorkflowAsync_ValidWorkflow_PerformsRollback()
    {
        // Arrange
        var workflowId = Guid.NewGuid().ToString();
        var workflow = _fixture.Build<DeploymentWorkflow>()
            .With(x => x.Id, workflowId)
            .With(x => x.RollbackConfiguration, new RollbackConfiguration { Enabled = true })
            .Create();

        _workflowRepository.GetWorkflowAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(workflow);

        _workflowExecutor.RollbackWorkflowAsync(
            Arg.Any<DeploymentWorkflow>(), 
            Arg.Any<string?>(), 
            Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _orchestratorService.RollbackWorkflowAsync(workflowId);

        // Assert
        result.Should().BeTrue();
        
        await _workflowRepository.Received().UpdateWorkflowAsync(
            Arg.Is<DeploymentWorkflow>(w => w.Status == WorkflowStatus.RollingBack),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RollbackWorkflowAsync_RollbackNotEnabled_ReturnsFalse()
    {
        // Arrange
        var workflowId = Guid.NewGuid().ToString();
        var workflow = _fixture.Build<DeploymentWorkflow>()
            .With(x => x.Id, workflowId)
            .With(x => x.RollbackConfiguration, new RollbackConfiguration { Enabled = false })
            .Create();

        _workflowRepository.GetWorkflowAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(workflow);

        // Act
        var result = await _orchestratorService.RollbackWorkflowAsync(workflowId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetWorkflowAsync_ExistingWorkflow_ReturnsFromCache()
    {
        // Arrange
        var workflowId = Guid.NewGuid().ToString();
        var cachedWorkflow = _fixture.Build<DeploymentWorkflow>()
            .With(x => x.Id, workflowId)
            .Create();

        _cacheService.GetAsync<DeploymentWorkflow>($"workflow:{workflowId}")
            .Returns(cachedWorkflow);

        // Act
        var result = await _orchestratorService.GetWorkflowAsync(workflowId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(workflowId);
        
        // Should not call repository since it was in cache
        await _workflowRepository.DidNotReceive().GetWorkflowAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetWorkflowAsync_NotInCache_ReturnsFromRepository()
    {
        // Arrange
        var workflowId = Guid.NewGuid().ToString();
        var workflow = _fixture.Build<DeploymentWorkflow>()
            .With(x => x.Id, workflowId)
            .Create();

        _cacheService.GetAsync<DeploymentWorkflow>($"workflow:{workflowId}")
            .Returns((DeploymentWorkflow?)null);

        _workflowRepository.GetWorkflowAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(workflow);

        // Act
        var result = await _orchestratorService.GetWorkflowAsync(workflowId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(workflowId);
        
        // Should cache the result
        await _cacheService.Received().SetAsync($"workflow:{workflowId}", workflow, TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task GetStatisticsAsync_ValidTimeRange_ReturnsStatistics()
    {
        // Arrange
        var workflows = _fixture.Build<DeploymentWorkflow>()
            .With(x => x.Status, WorkflowStatus.Completed)
            .With(x => x.StartedAt, DateTime.UtcNow.AddHours(-2))
            .With(x => x.CompletedAt, DateTime.UtcNow.AddHours(-1))
            .CreateMany(10)
            .ToList();

        // Add some failed workflows
        workflows.AddRange(_fixture.Build<DeploymentWorkflow>()
            .With(x => x.Status, WorkflowStatus.Failed)
            .CreateMany(2));

        _workflowRepository.GetWorkflowsAsync(Arg.Any<WorkflowFilter>(), Arg.Any<CancellationToken>())
            .Returns(workflows);

        // Act
        var result = await _orchestratorService.GetStatisticsAsync();

        // Assert
        result.Should().NotBeNull();
        result.TotalWorkflows.Should().Be(12);
        result.CompletedWorkflows.Should().Be(10);
        result.FailedWorkflows.Should().Be(2);
        result.SuccessRate.Should().BeApproximately(83.33, 0.01);
    }

    [Fact]
    public async Task GetHealthAsync_HealthyState_ReturnsHealthyStatus()
    {
        // Arrange
        var activeWorkflows = _fixture.Build<DeploymentWorkflow>()
            .With(x => x.Status, WorkflowStatus.Running)
            .CreateMany(5)
            .ToList();

        _workflowRepository.GetActiveWorkflowsAsync(Arg.Any<CancellationToken>())
            .Returns(activeWorkflows);

        // Act
        var result = await _orchestratorService.GetHealthAsync();

        // Assert
        result.Should().NotBeNull();
        result.IsHealthy.Should().BeTrue();
        result.Status.Should().Be("Healthy");
        result.ActiveWorkflows.Should().Be(5);
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHealthAsync_TooManyConcurrentWorkflows_ReturnsDegradedStatus()
    {
        // Arrange - Create more workflows than the configured maximum
        var activeWorkflows = _fixture.Build<DeploymentWorkflow>()
            .With(x => x.Status, WorkflowStatus.Running)
            .CreateMany(60) // More than MaxConcurrentWorkflows (50)
            .ToList();

        _workflowRepository.GetActiveWorkflowsAsync(Arg.Any<CancellationToken>())
            .Returns(activeWorkflows);

        // Act
        var result = await _orchestratorService.GetHealthAsync();

        // Assert
        result.Should().NotBeNull();
        result.IsHealthy.Should().BeFalse();
        result.Status.Should().Be("Degraded");
        result.Issues.Should().Contain(issue => issue.Contains("Too many concurrent workflows"));
    }

    [Fact]
    public async Task CreateWorkflowAsync_ProductionScaleScenario_HandlesLargeServerList()
    {
        // Arrange - Create a deployment for 250 servers
        var targetServers = Enumerable.Range(1, 250)
            .Select(i => $"prod-server-{i:D3}")
            .ToList();

        var request = _fixture.Build<DeploymentWorkflowRequest>()
            .With(x => x.Strategy, DeploymentStrategy.Rolling)
            .With(x => x.TargetServers, targetServers)
            .With(x => x.ServiceName, "ProductionCriticalService")
            .With(x => x.Timeout, TimeSpan.FromHours(6)) // Longer timeout for large deployment
            .Create();

        var workflowId = Guid.NewGuid().ToString();
        _workflowRepository.CreateWorkflowAsync(Arg.Any<DeploymentWorkflow>(), Arg.Any<CancellationToken>())
            .Returns(workflowId);

        // Act
        var result = await _orchestratorService.CreateWorkflowAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.TargetServers.Should().HaveCount(250);
        result.ServiceName.Should().Be("ProductionCriticalService");
        result.Timeout.Should().Be(TimeSpan.FromHours(6));
    }

    [Theory]
    [InlineData(DeploymentStrategy.Rolling)]
    [InlineData(DeploymentStrategy.BlueGreen)]
    [InlineData(DeploymentStrategy.Canary)]
    [InlineData(DeploymentStrategy.Immediate)]
    public async Task CreateWorkflowAsync_AllSupportedStrategies_CreatesWorkflowSuccessfully(DeploymentStrategy strategy)
    {
        // Arrange
        var request = _fixture.Build<DeploymentWorkflowRequest>()
            .With(x => x.Strategy, strategy)
            .Create();

        var workflowId = Guid.NewGuid().ToString();
        _workflowRepository.CreateWorkflowAsync(Arg.Any<DeploymentWorkflow>(), Arg.Any<CancellationToken>())
            .Returns(workflowId);

        // Act
        var result = await _orchestratorService.CreateWorkflowAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Strategy.Should().Be(strategy);
    }

    [Fact]
    public async Task AutoRollbackAsync_EnabledAutoRollback_TriggersRollback()
    {
        // Arrange
        var workflowId = Guid.NewGuid().ToString();
        var workflow = _fixture.Build<DeploymentWorkflow>()
            .With(x => x.Id, workflowId)
            .With(x => x.RollbackConfiguration, new RollbackConfiguration 
            { 
                Enabled = true, 
                AutomaticRollback = true 
            })
            .Create();

        _workflowRepository.GetWorkflowAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(workflow);

        _workflowExecutor.RollbackWorkflowAsync(
            Arg.Any<DeploymentWorkflow>(), 
            Arg.Any<string?>(), 
            Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _orchestratorService.AutoRollbackAsync(
            workflowId, 
            RollbackTriggerType.HealthCheckFailure, 
            "Health check failed");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void GetStrategy_ExistingStrategy_ReturnsCorrectStrategy()
    {
        // Act
        var strategy = _orchestratorService.GetStrategy(DeploymentStrategy.Rolling);

        // Assert
        strategy.Should().NotBeNull();
        strategy.StrategyType.Should().Be(DeploymentStrategy.Rolling);
    }

    [Fact]
    public void GetStrategy_NonExistentStrategy_ThrowsArgumentException()
    {
        // Act & Assert
        var act = () => _orchestratorService.GetStrategy(DeploymentStrategy.Scheduled);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Strategy Scheduled not found*");
    }

    private IDeploymentStrategy CreateMockStrategy(DeploymentStrategy strategyType)
    {
        var strategy = Substitute.For<IDeploymentStrategy>();
        strategy.StrategyType.Returns(strategyType);
        
        strategy.ValidateConfigurationAsync(Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(true);

        strategy.CreatePhasesAsync(Arg.Any<DeploymentWorkflowRequest>(), Arg.Any<CancellationToken>())
            .Returns(_fixture.CreateMany<DeploymentPhase>(3).ToList());

        strategy.EstimateExecutionTimeAsync(
            Arg.Any<List<string>>(), 
            Arg.Any<Dictionary<string, object>>(), 
            Arg.Any<CancellationToken>())
            .Returns(TimeSpan.FromHours(2));

        return strategy;
    }

    public void Dispose()
    {
        _orchestratorService?.Dispose();
    }
}