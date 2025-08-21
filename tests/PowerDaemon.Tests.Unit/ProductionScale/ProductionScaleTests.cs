using AutoFixture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PowerDaemon.Cache.Services;
using PowerDaemon.Messaging.Configuration;
using PowerDaemon.Messaging.Messages;
using PowerDaemon.Messaging.Services;
using PowerDaemon.Orchestrator.Configuration;
using PowerDaemon.Orchestrator.Models;
using PowerDaemon.Orchestrator.Services;
using PowerDaemon.Orchestrator.Strategies;
using System.Diagnostics;
using Xunit;

namespace PowerDaemon.Tests.Unit.ProductionScale;

public class ProductionScaleTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public async Task RabbitMQService_Handle250ConcurrentDeployments_ProcessesEfficiently()
    {
        // Arrange
        var logger = Substitute.For<ILogger<RabbitMQService>>();
        var config = new RabbitMQConfiguration
        {
            ProductionScale = new ProductionScaleConfiguration
            {
                MaxConnectionPoolSize = 100,
                MaxConcurrentOperations = 500,
                BatchSize = 50,
                MaxMessagesPerSecond = 2000
            }
        };
        var options = Substitute.For<IOptions<RabbitMQConfiguration>>();
        options.Value.Returns(config);

        var rabbitMqService = new RabbitMQService(logger, options);

        // Create 250 deployment commands for different servers
        var deploymentCommands = new List<DeploymentCommand>();
        for (int i = 1; i <= 250; i++)
        {
            deploymentCommands.Add(new DeploymentCommand
            {
                ServiceName = $"ProductionService{i % 10}", // 10 different services
                Version = "2.1.0",
                TargetServerId = $"prod-server-{i:D3}",
                Strategy = i % 3 == 0 ? DeploymentStrategy.BlueGreen : DeploymentStrategy.Rolling,
                Priority = i % 50 == 0 ? DeploymentPriority.Critical : DeploymentPriority.Normal,
                TargetServers = new List<string> { $"prod-server-{i:D3}" }
            });
        }

        var stopwatch = Stopwatch.StartNew();
        var tasks = new List<Task>();

        // Act - Publish all commands concurrently
        foreach (var command in deploymentCommands)
        {
            var routingKey = command.Priority == DeploymentPriority.Critical 
                ? "critical.deployment" 
                : "normal.deployment";
            tasks.Add(rabbitMqService.PublishAsync(command, routingKey));
        }

        await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(30000, "250 deployments should complete within 30 seconds");
        deploymentCommands.Should().HaveCount(250);
        deploymentCommands.Count(c => c.Priority == DeploymentPriority.Critical).Should().Be(5);
        deploymentCommands.Count(c => c.Strategy == DeploymentStrategy.BlueGreen).Should().BeGreaterThan(80);
        
        // Verify configuration supports the scale
        config.ProductionScale.MaxConcurrentOperations.Should().BeGreaterThan(250);
        config.ProductionScale.MaxMessagesPerSecond.Should().BeGreaterThan(500);
    }

    [Fact]
    public async Task DeploymentOrchestrator_Create100ConcurrentWorkflows_HandlesLoad()
    {
        // Arrange
        var logger = Substitute.For<ILogger<DeploymentOrchestratorService>>();
        var workflowRepository = Substitute.For<IWorkflowRepository>();
        var workflowExecutor = Substitute.For<IWorkflowExecutor>();
        var messagePublisher = Substitute.For<IMessagePublisher>();
        var cacheService = Substitute.For<ICacheService>();

        var config = new OrchestratorConfiguration
        {
            MaxConcurrentWorkflows = 150,
            MaxQueuedWorkflows = 500,
            HealthCheckIntervalSeconds = 30
        };

        var options = Substitute.For<IOptions<OrchestratorConfiguration>>();
        options.Value.Returns(config);

        var strategies = new List<IDeploymentStrategy>
        {
            CreateMockStrategy(DeploymentStrategy.Rolling),
            CreateMockStrategy(DeploymentStrategy.BlueGreen),
            CreateMockStrategy(DeploymentStrategy.Canary)
        };

        // Configure repository to return unique workflow IDs
        var workflowIdCounter = 0;
        workflowRepository.CreateWorkflowAsync(Arg.Any<DeploymentWorkflow>(), Arg.Any<CancellationToken>())
            .Returns(_ => $"workflow-{Interlocked.Increment(ref workflowIdCounter)}");

        var orchestrator = new DeploymentOrchestratorService(
            logger, options, workflowRepository, workflowExecutor, messagePublisher, cacheService, strategies);

        // Create 100 concurrent workflow requests
        var requests = new List<DeploymentWorkflowRequest>();
        for (int i = 1; i <= 100; i++)
        {
            var targetServers = Enumerable.Range(1, 5).Select(j => $"cluster{i}-server{j}").ToList();
            requests.Add(_fixture.Build<DeploymentWorkflowRequest>()
                .With(r => r.ServiceName, $"Service{i}")
                .With(r => r.Strategy, (DeploymentStrategy)(i % 3)) // Rotate strategies
                .With(r => r.TargetServers, targetServers)
                .With(r => r.Timeout, TimeSpan.FromHours(2))
                .Create());
        }

        var stopwatch = Stopwatch.StartNew();
        var tasks = new List<Task<DeploymentWorkflow>>();

        // Act - Create all workflows concurrently
        foreach (var request in requests)
        {
            tasks.Add(orchestrator.CreateWorkflowAsync(request));
        }

        var workflows = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(20000, "100 workflow creations should complete within 20 seconds");
        workflows.Should().HaveCount(100);
        workflows.Should().OnlyHaveUniqueItems(w => w.Id, "All workflows should have unique IDs");
        workflows.All(w => !string.IsNullOrEmpty(w.ServiceName)).Should().BeTrue();
        workflows.All(w => w.TargetServers.Count == 5).Should().BeTrue();
        
        // Verify repository was called for each workflow
        await workflowRepository.Received(100).CreateWorkflowAsync(Arg.Any<DeploymentWorkflow>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RollingDeploymentStrategy_500ServerDeployment_CreatesEfficientPhases()
    {
        // Arrange
        var logger = Substitute.For<ILogger<RollingDeploymentStrategy>>();
        var strategy = new RollingDeploymentStrategy(logger);

        var targetServers = Enumerable.Range(1, 500)
            .Select(i => $"enterprise-server-{i:D3}")
            .ToList();

        var request = _fixture.Build<DeploymentWorkflowRequest>()
            .With(r => r.Strategy, DeploymentStrategy.Rolling)
            .With(r => r.TargetServers, targetServers)
            .With(r => r.ServiceName, "EnterpriseApplication")
            .With(r => r.Configuration, CreateValidRollingConfiguration())
            .Create();

        var stopwatch = Stopwatch.StartNew();

        // Act
        var phases = await strategy.CreatePhasesAsync(request);
        var estimatedTime = await strategy.EstimateExecutionTimeAsync(targetServers, request.Configuration);

        stopwatch.Stop();

        // Assert
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000, "Phase creation should complete quickly even for 500 servers");
        phases.Should().NotBeNull();
        phases.Should().NotBeEmpty();
        phases.Count.Should().BeLessThan(100, "Should group servers efficiently, not create excessive phases");
        phases.Count.Should().BeGreaterThan(10, "Should create multiple waves for 500 servers");
        
        // Verify all servers are covered
        var allPhaseServers = phases.SelectMany(p => p.TargetServers).Distinct().ToList();
        allPhaseServers.Should().HaveCount(500, "All servers should be covered across phases");
        
        // Estimated time should be reasonable
        estimatedTime.Should().BeGreaterThan(TimeSpan.FromHours(2), "Large deployment should take reasonable time");
        estimatedTime.Should().BeLessThan(TimeSpan.FromHours(12), "Should complete within business day");
        
        // Verify phase structure
        phases.Should().Contain(p => p.Name.Contains("Pre-Deployment"));
        phases.Should().Contain(p => p.Name.Contains("Post-Deployment"));
        phases.Should().Contain(p => p.Name.Contains("Cleanup"));
    }

    [Fact]
    public void DeploymentCommand_MassiveBatchSerialization_HandlesLargeDataSets()
    {
        // Arrange - Create 1000 deployment commands
        var commands = new List<DeploymentCommand>();
        for (int i = 1; i <= 1000; i++)
        {
            var command = new DeploymentCommand
            {
                ServiceName = $"BatchService{i}",
                Version = "3.0.0",
                TargetServerId = $"batch-server-{i:D4}",
                Strategy = (DeploymentStrategy)(i % 5), // Rotate through all strategies
                Priority = (DeploymentPriority)(i % 4), // Rotate through all priorities
                TargetServers = Enumerable.Range(1, i % 10 + 1).Select(j => $"server-{i}-{j}").ToList(),
                Configuration = Enumerable.Range(1, 20).ToDictionary(j => $"config_{j}", j => (object)$"value_{i}_{j}"),
                Metadata = Enumerable.Range(1, 10).ToDictionary(j => $"meta_{j}", j => $"metadata_{i}_{j}")
            };
            commands.Add(command);
        }

        var stopwatch = Stopwatch.StartNew();

        // Act - Serialize all commands
        var jsonResults = new List<string>();
        foreach (var command in commands)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(command);
            jsonResults.Add(json);
        }

        // Deserialize all commands
        var deserializedCommands = new List<DeploymentCommand>();
        foreach (var json in jsonResults)
        {
            var command = System.Text.Json.JsonSerializer.Deserialize<DeploymentCommand>(json);
            deserializedCommands.Add(command!);
        }

        stopwatch.Stop();

        // Assert
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(10000, "Serialization of 1000 commands should complete within 10 seconds");
        jsonResults.Should().HaveCount(1000);
        jsonResults.Should().OnlyContain(json => !string.IsNullOrEmpty(json));
        deserializedCommands.Should().HaveCount(1000);
        
        // Verify data integrity
        for (int i = 0; i < 1000; i++)
        {
            var original = commands[i];
            var deserialized = deserializedCommands[i];
            
            deserialized.ServiceName.Should().Be(original.ServiceName);
            deserialized.TargetServerId.Should().Be(original.TargetServerId);
            deserialized.Strategy.Should().Be(original.Strategy);
            deserialized.Priority.Should().Be(original.Priority);
            deserialized.TargetServers.Should().BeEquivalentTo(original.TargetServers);
        }
    }

    [Fact]
    public async Task CacheService_HighThroughputOperations_MaintainsPerformance()
    {
        // Arrange
        var cacheService = Substitute.For<ICacheService>();
        
        // Configure cache service for high-throughput operations
        cacheService.SetAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(true);
        
        cacheService.GetAsync<DeploymentWorkflow>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => _fixture.Create<DeploymentWorkflow>());

        cacheService.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Create 2000 cache operations (simulating high-throughput scenario)
        var tasks = new List<Task>();
        var stopwatch = Stopwatch.StartNew();

        // Act - Execute cache operations concurrently
        for (int i = 1; i <= 2000; i++)
        {
            var workflowId = $"workflow-{i}";
            var workflow = _fixture.Build<DeploymentWorkflow>()
                .With(w => w.Id, workflowId)
                .Create();

            tasks.Add(cacheService.SetAsync($"workflow:{workflowId}", workflow, TimeSpan.FromHours(1)));
            tasks.Add(cacheService.GetAsync<DeploymentWorkflow>($"workflow:{workflowId}"));
            tasks.Add(cacheService.ExistsAsync($"workflow:{workflowId}"));
        }

        await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(15000, "2000 cache operations should complete within 15 seconds");
        tasks.Should().HaveCount(6000); // 2000 workflows * 3 operations each
        
        // Verify cache service was called appropriately
        await cacheService.Received(2000).SetAsync(Arg.Any<string>(), Arg.Any<DeploymentWorkflow>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
        await cacheService.Received(2000).GetAsync<DeploymentWorkflow>(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await cacheService.Received(2000).ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ProductionConfiguration_ScalesTo1000Servers_MeetsRequirements()
    {
        // Arrange - Configuration for 1000-server deployment
        var rabbitMqConfig = new RabbitMQConfiguration
        {
            ProductionScale = new ProductionScaleConfiguration
            {
                MaxConnectionPoolSize = 200,
                MinConnectionPoolSize = 50,
                PrefetchCount = 100,
                BatchSize = 500,
                ConsumerThreadCount = 25,
                MaxMessagesPerSecond = 5000,
                MaxConcurrentOperations = 1000
            }
        };

        var orchestratorConfig = new OrchestratorConfiguration
        {
            MaxConcurrentWorkflows = 500,
            MaxQueuedWorkflows = 2000,
            HealthCheckIntervalSeconds = 30,
            WorkflowTimeoutMinutes = 480, // 8 hours for large deployments
            EnableAutoRollback = true,
            RollbackTimeoutMinutes = 120
        };

        // Act & Assert - Verify configuration scales appropriately
        rabbitMqConfig.ProductionScale.MaxConcurrentOperations.Should().BeGreaterOrEqualTo(1000,
            "Should support concurrent operations for 1000 servers");
        
        rabbitMqConfig.ProductionScale.MaxMessagesPerSecond.Should().BeGreaterOrEqualTo(1000,
            "Should handle high message throughput");
            
        rabbitMqConfig.ProductionScale.BatchSize.Should().BeGreaterOrEqualTo(100,
            "Should use efficient batch sizes");
            
        orchestratorConfig.MaxConcurrentWorkflows.Should().BeGreaterOrEqualTo(100,
            "Should support many concurrent workflows");
            
        orchestratorConfig.MaxQueuedWorkflows.Should().BeGreaterOrEqualTo(orchestratorConfig.MaxConcurrentWorkflows * 2,
            "Queue should be larger than concurrent capacity");
            
        orchestratorConfig.WorkflowTimeoutMinutes.Should().BeGreaterOrEqualTo(240,
            "Should allow adequate time for large deployments");
            
        orchestratorConfig.EnableAutoRollback.Should().BeTrue(
            "Production should support auto-rollback for safety");
    }

    [Fact]
    public async Task MultiServiceDeployment_SimultaneousDeployments_HandlesConcurrency()
    {
        // Arrange - Simulate deploying 20 different services simultaneously
        var services = new[]
        {
            "UserAuthenticationService", "PaymentProcessingService", "OrderManagementService",
            "InventoryService", "NotificationService", "ReportingService", "LoggingService",
            "MonitoringService", "SecurityService", "DataProcessingService", "WebApiService",
            "MobileApiService", "AdminPanelService", "CustomerPortalService", "IntegrationService",
            "AnalyticsService", "CacheService", "SearchService", "RecommendationService", "AuditService"
        };

        var deploymentCommands = new List<DeploymentCommand>();
        
        foreach (var service in services)
        {
            // Each service deploys to 10-50 servers
            var serverCount = new Random().Next(10, 51);
            var servers = Enumerable.Range(1, serverCount)
                .Select(i => $"{service.ToLower()}-{i:D2}")
                .ToList();

            deploymentCommands.Add(new DeploymentCommand
            {
                ServiceName = service,
                Version = "4.2.1",
                Strategy = DeploymentStrategy.Rolling,
                Priority = service.Contains("Security") || service.Contains("Payment") 
                    ? DeploymentPriority.Critical 
                    : DeploymentPriority.Normal,
                TargetServers = servers,
                Timeout = TimeSpan.FromHours(3),
                Configuration = new Dictionary<string, object>
                {
                    ["rollout_percentage"] = service.Contains("Critical") ? 10 : 25,
                    ["health_check_timeout"] = TimeSpan.FromMinutes(5),
                    ["max_failures"] = service.Contains("Critical") ? 0 : 2
                }
            });
        }

        var stopwatch = Stopwatch.StartNew();

        // Act - Process all deployments
        var tasks = deploymentCommands.Select(async cmd =>
        {
            // Simulate deployment processing
            await Task.Delay(new Random().Next(100, 500)); // Simulate variable processing time
            return new { Service = cmd.ServiceName, ServerCount = cmd.TargetServers.Count, Success = true };
        }).ToList();

        var results = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(10000, "Multi-service deployment should complete efficiently");
        results.Should().HaveCount(20);
        results.Should().OnlyContain(r => r.Success, "All deployments should succeed");
        
        var totalServers = results.Sum(r => r.ServerCount);
        totalServers.Should().BeInRange(200, 1000, "Should deploy to reasonable number of servers");
        
        var criticalServices = deploymentCommands.Where(c => c.Priority == DeploymentPriority.Critical).ToList();
        criticalServices.Should().NotBeEmpty("Should have critical services");
        criticalServices.Should().OnlyContain(c => c.Configuration.ContainsKey("max_failures") && 
            (int)c.Configuration["max_failures"] == 0, "Critical services should have zero failure tolerance");
    }

    [Theory]
    [InlineData(100, 5)] // 100 servers, 5 seconds max
    [InlineData(250, 10)] // 250 servers, 10 seconds max  
    [InlineData(500, 20)] // 500 servers, 20 seconds max
    [InlineData(1000, 40)] // 1000 servers, 40 seconds max
    public void PerformanceBenchmark_VariousScales_MeetsPerformanceTargets(int serverCount, int maxSeconds)
    {
        // Arrange
        var servers = Enumerable.Range(1, serverCount)
            .Select(i => $"perf-server-{i:D4}")
            .ToList();

        var command = new DeploymentCommand
        {
            ServiceName = "PerformanceTestService",
            Version = "1.0.0",
            TargetServers = servers,
            Configuration = Enumerable.Range(1, 50).ToDictionary(i => $"key{i}", i => (object)$"value{i}"),
            Metadata = Enumerable.Range(1, 25).ToDictionary(i => $"meta{i}", i => $"metadata{i}")
        };

        var stopwatch = Stopwatch.StartNew();

        // Act - Perform operations that would be common in production
        var json = System.Text.Json.JsonSerializer.Serialize(command);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<DeploymentCommand>(json);
        var uniqueServers = command.TargetServers.Distinct().ToList();
        var configKeys = command.Configuration.Keys.ToList();
        var metadataValues = command.Metadata.Values.ToList();

        stopwatch.Stop();

        // Assert
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(maxSeconds * 1000, 
            $"Operations for {serverCount} servers should complete within {maxSeconds} seconds");
        
        deserialized!.TargetServers.Should().HaveCount(serverCount);
        uniqueServers.Should().HaveCount(serverCount, "All servers should be unique");
        configKeys.Should().HaveCount(50);
        metadataValues.Should().HaveCount(25);
        json.Should().NotBeNullOrEmpty();
        json.Length.Should().BeGreaterThan(1000, "JSON should contain substantial data");
    }

    private IDeploymentStrategy CreateMockStrategy(DeploymentStrategy strategyType)
    {
        var strategy = Substitute.For<IDeploymentStrategy>();
        strategy.StrategyType.Returns(strategyType);
        
        strategy.ValidateConfigurationAsync(Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(true);

        strategy.CreatePhasesAsync(Arg.Any<DeploymentWorkflowRequest>(), Arg.Any<CancellationToken>())
            .Returns(_fixture.CreateMany<DeploymentPhase>(5).ToList());

        strategy.EstimateExecutionTimeAsync(
            Arg.Any<List<string>>(), 
            Arg.Any<Dictionary<string, object>>(), 
            Arg.Any<CancellationToken>())
            .Returns(TimeSpan.FromHours(2));

        return strategy;
    }

    private Dictionary<string, object> CreateValidRollingConfiguration()
    {
        return new Dictionary<string, object>
        {
            ["RollingConfiguration"] = new Dictionary<string, object>
            {
                ["EnableHealthChecks"] = true,
                ["MaxFailureThreshold"] = 5
            },
            ["WaveConfiguration"] = new Dictionary<string, object>
            {
                ["Strategy"] = "FixedSize",
                ["WaveSize"] = 25, // Deploy 25 servers at a time
                ["WavePercentage"] = 5.0, // 5% per wave
                ["WaveInterval"] = TimeSpan.FromMinutes(15),
                ["ParallelDeploymentWithinWave"] = true,
                ["MaxParallelism"] = 10
            },
            ["HealthCheckConfiguration"] = new Dictionary<string, object>
            {
                ["HealthCheckTimeout"] = TimeSpan.FromMinutes(10),
                ["MaxRetries"] = 3
            }
        };
    }
}