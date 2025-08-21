using AutoFixture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PowerDaemon.Messaging.Messages;
using PowerDaemon.Orchestrator.Models;
using PowerDaemon.Orchestrator.Strategies;
using Xunit;

namespace PowerDaemon.Tests.Unit.Orchestrator;

public class RollingDeploymentStrategyTests
{
    private readonly RollingDeploymentStrategy _strategy;
    private readonly ILogger<RollingDeploymentStrategy> _logger;
    private readonly Fixture _fixture = new();

    public RollingDeploymentStrategyTests()
    {
        _logger = Substitute.For<ILogger<RollingDeploymentStrategy>>();
        _strategy = new RollingDeploymentStrategy(_logger);
    }

    [Fact]
    public void RollingDeploymentStrategy_StrategyType_IsRolling()
    {
        // Assert
        _strategy.StrategyType.Should().Be(DeploymentStrategy.Rolling);
    }

    [Fact]
    public async Task CreatePhasesAsync_ValidRequest_CreatesPhases()
    {
        // Arrange
        var request = CreateValidDeploymentWorkflowRequest(4);

        // Act
        var phases = await _strategy.CreatePhasesAsync(request);

        // Assert
        phases.Should().NotBeNull();
        phases.Should().NotBeEmpty();
        
        // Should have pre-deployment, validation, deployment waves, and post-deployment phases
        phases.Should().Contain(p => p.Name.Contains("Pre-Deployment"));
        phases.Should().Contain(p => p.Name.Contains("Post-Deployment"));
        phases.Should().Contain(p => p.Name.Contains("Cleanup"));
    }

    [Fact]
    public async Task CreatePhasesAsync_MultipleServers_CreatesMultipleWaves()
    {
        // Arrange - 12 servers should create multiple waves with default settings
        var request = CreateValidDeploymentWorkflowRequest(12);

        // Act
        var phases = await _strategy.CreatePhasesAsync(request);

        // Assert
        phases.Should().NotBeNull();
        phases.Count.Should().BeGreaterThan(5); // At least pre, validation, waves, post, cleanup
        
        var deploymentPhases = phases.Where(p => p.Name.Contains("Wave") && p.Name.Contains("Deployment"));
        deploymentPhases.Should().NotBeEmpty("Should have wave deployment phases");
    }

    [Fact]
    public async Task CreatePhasesAsync_ProductionScale_Handles250Servers()
    {
        // Arrange - Production scale with 250 servers
        var targetServers = Enumerable.Range(1, 250)
            .Select(i => $"prod-server-{i:D3}")
            .ToList();

        var request = CreateValidDeploymentWorkflowRequest(targetServers);

        // Act
        var phases = await _strategy.CreatePhasesAsync(request);

        // Assert
        phases.Should().NotBeNull();
        phases.Should().NotBeEmpty();
        
        // Verify all servers are covered across all phases
        var allPhaseServers = phases.SelectMany(p => p.TargetServers).ToList();
        allPhaseServers.Should().Contain("prod-server-001");
        allPhaseServers.Should().Contain("prod-server-250");
        
        // Should have reasonable number of phases (not one per server)
        phases.Count.Should().BeLessThan(50, "Should group servers into waves efficiently");
    }

    [Fact]
    public async Task ValidateConfigurationAsync_ValidConfiguration_ReturnsTrue()
    {
        // Arrange
        var configuration = CreateValidRollingConfiguration();

        // Act
        var result = await _strategy.ValidateConfigurationAsync(configuration);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateConfigurationAsync_MissingRequiredKeys_ReturnsFalse()
    {
        // Arrange
        var configuration = new Dictionary<string, object>
        {
            ["SomeOtherKey"] = "value"
            // Missing required keys: RollingConfiguration, WaveConfiguration, HealthCheckConfiguration
        };

        // Act
        var result = await _strategy.ValidateConfigurationAsync(configuration);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("RollingConfiguration")]
    [InlineData("WaveConfiguration")]
    [InlineData("HealthCheckConfiguration")]
    public async Task ValidateConfigurationAsync_MissingSpecificKey_ReturnsFalse(string missingKey)
    {
        // Arrange
        var configuration = CreateValidRollingConfiguration();
        configuration.Remove(missingKey);

        // Act
        var result = await _strategy.ValidateConfigurationAsync(configuration);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateConfigurationAsync_InvalidWaveStrategy_ReturnsFalse()
    {
        // Arrange
        var configuration = CreateValidRollingConfiguration();
        var waveConfig = (Dictionary<string, object>)configuration["WaveConfiguration"];
        waveConfig["Strategy"] = "InvalidStrategy";

        // Act
        var result = await _strategy.ValidateConfigurationAsync(configuration);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-10)]
    public async Task ValidateConfigurationAsync_InvalidWaveSize_ReturnsFalse(int invalidWaveSize)
    {
        // Arrange
        var configuration = CreateValidRollingConfiguration();
        var waveConfig = (Dictionary<string, object>)configuration["WaveConfiguration"];
        waveConfig["WaveSize"] = invalidWaveSize;

        // Act
        var result = await _strategy.ValidateConfigurationAsync(configuration);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(0.0)]
    [InlineData(101.0)]
    [InlineData(150.0)]
    public async Task ValidateConfigurationAsync_InvalidWavePercentage_ReturnsFalse(double invalidPercentage)
    {
        // Arrange
        var configuration = CreateValidRollingConfiguration();
        var waveConfig = (Dictionary<string, object>)configuration["WaveConfiguration"];
        waveConfig["WavePercentage"] = invalidPercentage;

        // Act
        var result = await _strategy.ValidateConfigurationAsync(configuration);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(5, 25)]
    [InlineData(10, 50)]
    public async Task EstimateExecutionTimeAsync_VariousServerCounts_ReturnsReasonableTime(
        int serverCount, 
        int expectedMinimumMinutes)
    {
        // Arrange
        var targetServers = Enumerable.Range(1, serverCount)
            .Select(i => $"server-{i}")
            .ToList();
        var configuration = CreateValidRollingConfiguration();

        // Act
        var estimatedTime = await _strategy.EstimateExecutionTimeAsync(targetServers, configuration);

        // Assert
        estimatedTime.Should().BeGreaterThan(TimeSpan.FromMinutes(expectedMinimumMinutes));
        estimatedTime.Should().BeLessThan(TimeSpan.FromHours(12), "Should not take more than 12 hours");
    }

    [Fact]
    public async Task EstimateExecutionTimeAsync_ProductionScale250Servers_ReturnsReasonableTime()
    {
        // Arrange
        var targetServers = Enumerable.Range(1, 250)
            .Select(i => $"prod-server-{i:D3}")
            .ToList();
        var configuration = CreateValidRollingConfiguration();

        // Act
        var estimatedTime = await _strategy.EstimateExecutionTimeAsync(targetServers, configuration);

        // Assert
        estimatedTime.Should().BeGreaterThan(TimeSpan.FromHours(1), "Large deployment should take reasonable time");
        estimatedTime.Should().BeLessThan(TimeSpan.FromHours(8), "Should complete within business hours");
    }

    [Fact]
    public async Task CreatePhasesAsync_PreDeploymentPhase_HasRequiredSteps()
    {
        // Arrange
        var request = CreateValidDeploymentWorkflowRequest(4);

        // Act
        var phases = await _strategy.CreatePhasesAsync(request);

        // Assert
        var preDeploymentPhase = phases.FirstOrDefault(p => p.Name.Contains("Pre-Deployment"));
        preDeploymentPhase.Should().NotBeNull();
        preDeploymentPhase!.Steps.Should().NotBeEmpty();
        
        // Should have validation steps
        preDeploymentPhase.Steps.Should().Contain(s => s.Type == StepType.Validation);
        preDeploymentPhase.Steps.Should().Contain(s => s.Name.Contains("Environment"));
        preDeploymentPhase.Steps.Should().Contain(s => s.Name.Contains("Load Balancer"));
    }

    [Fact]
    public async Task CreatePhasesAsync_PostDeploymentPhase_HasValidationSteps()
    {
        // Arrange
        var request = CreateValidDeploymentWorkflowRequest(4);

        // Act
        var phases = await _strategy.CreatePhasesAsync(request);

        // Assert
        var postDeploymentPhase = phases.FirstOrDefault(p => p.Name.Contains("Post-Deployment"));
        postDeploymentPhase.Should().NotBeNull();
        postDeploymentPhase!.Steps.Should().NotBeEmpty();
        
        // Should have health check and integration test steps
        postDeploymentPhase.Steps.Should().Contain(s => s.Type == StepType.HealthCheck);
        postDeploymentPhase.Steps.Should().Contain(s => s.Name.Contains("Integration Tests"));
    }

    [Fact]
    public async Task CreatePhasesAsync_CleanupPhase_HasNonCriticalSteps()
    {
        // Arrange
        var request = CreateValidDeploymentWorkflowRequest(4);

        // Act
        var phases = await _strategy.CreatePhasesAsync(request);

        // Assert
        var cleanupPhase = phases.FirstOrDefault(p => p.Name.Contains("Cleanup"));
        cleanupPhase.Should().NotBeNull();
        cleanupPhase!.Steps.Should().NotBeEmpty();
        cleanupPhase.RollbackOnFailure.Should().BeFalse("Cleanup phase should not trigger rollback");
        
        // Cleanup steps should generally not be critical
        var nonCriticalSteps = cleanupPhase.Steps.Where(s => 
            s.Parameters.ContainsKey("Critical") && 
            (bool)s.Parameters["Critical"] == false);
        nonCriticalSteps.Should().NotBeEmpty("Should have non-critical cleanup steps");
    }

    [Fact]
    public async Task CreatePhasesAsync_WaveDeploymentPhases_HaveCorrectTargetServers()
    {
        // Arrange
        var targetServers = new List<string> { "server1", "server2", "server3", "server4" };
        var request = CreateValidDeploymentWorkflowRequest(targetServers);

        // Act
        var phases = await _strategy.CreatePhasesAsync(request);

        // Assert
        var wavePhases = phases.Where(p => p.Name.Contains("Wave") && p.Name.Contains("Deployment")).ToList();
        wavePhases.Should().NotBeEmpty();
        
        // All servers should be covered across all wave phases
        var allWaveServers = wavePhases.SelectMany(p => p.TargetServers).Distinct().ToList();
        allWaveServers.Should().BeEquivalentTo(targetServers);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(100)]
    public async Task CreatePhasesAsync_VariousServerCounts_CreatesAppropriateWaves(int serverCount)
    {
        // Arrange
        var request = CreateValidDeploymentWorkflowRequest(serverCount);

        // Act
        var phases = await _strategy.CreatePhasesAsync(request);

        // Assert
        phases.Should().NotBeNull();
        phases.Should().NotBeEmpty();
        
        var deploymentPhases = phases.Where(p => p.Name.Contains("Wave") && p.Name.Contains("Deployment")).ToList();
        
        // Should not have more waves than servers
        deploymentPhases.Count.Should().BeLessOrEqualTo(serverCount);
        
        // Should not deploy to all servers in a single wave (unless very few servers)
        if (serverCount > 4)
        {
            deploymentPhases.Count.Should().BeGreaterThan(1, "Should use multiple waves for larger deployments");
        }
    }

    [Fact]
    public async Task CreatePhasesAsync_ExceptionDuringPhaseCreation_ThrowsException()
    {
        // Arrange - Create request with invalid configuration that might cause issues
        var request = _fixture.Build<DeploymentWorkflowRequest>()
            .With(x => x.TargetServers, new List<string>()) // Empty server list
            .With(x => x.Configuration, new Dictionary<string, object>()) // Empty configuration
            .Create();

        // Act & Assert
        var act = () => _strategy.CreatePhasesAsync(request);
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task ValidateConfigurationAsync_ExceptionDuringValidation_ReturnsFalse()
    {
        // Arrange - Configuration with null values that might cause exceptions
        var configuration = new Dictionary<string, object>
        {
            ["RollingConfiguration"] = null!,
            ["WaveConfiguration"] = new Dictionary<string, object>(),
            ["HealthCheckConfiguration"] = new Dictionary<string, object>()
        };

        // Act
        var result = await _strategy.ValidateConfigurationAsync(configuration);

        // Assert
        result.Should().BeFalse();
    }

    private DeploymentWorkflowRequest CreateValidDeploymentWorkflowRequest(int serverCount)
    {
        var targetServers = Enumerable.Range(1, serverCount)
            .Select(i => $"server-{i}")
            .ToList();

        return CreateValidDeploymentWorkflowRequest(targetServers);
    }

    private DeploymentWorkflowRequest CreateValidDeploymentWorkflowRequest(List<string> targetServers)
    {
        return _fixture.Build<DeploymentWorkflowRequest>()
            .With(x => x.Strategy, DeploymentStrategy.Rolling)
            .With(x => x.TargetServers, targetServers)
            .With(x => x.Configuration, CreateValidRollingConfiguration())
            .Create();
    }

    private Dictionary<string, object> CreateValidRollingConfiguration()
    {
        return new Dictionary<string, object>
        {
            ["RollingConfiguration"] = new Dictionary<string, object>
            {
                ["EnableHealthChecks"] = true,
                ["MaxFailureThreshold"] = 10
            },
            ["WaveConfiguration"] = new Dictionary<string, object>
            {
                ["Strategy"] = WaveStrategy.FixedSize.ToString(),
                ["WaveSize"] = 2,
                ["WavePercentage"] = 25.0,
                ["WaveInterval"] = TimeSpan.FromMinutes(10),
                ["ParallelDeploymentWithinWave"] = false,
                ["MaxParallelism"] = 5,
                ["DelayBetweenServers"] = TimeSpan.FromMinutes(2)
            },
            ["HealthCheckConfiguration"] = new Dictionary<string, object>
            {
                ["HealthCheckTimeout"] = TimeSpan.FromMinutes(5),
                ["MaxRetries"] = 3
            }
        };
    }
}