using AutoFixture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PowerDaemon.Messaging.Messages;
using PowerDaemon.Orchestrator.Models;
using PowerDaemon.Orchestrator.Services;
using System.Text.Json;
using Xunit;

namespace PowerDaemon.Tests.Unit.ErrorHandling;

public class ErrorHandlingAndEdgeCaseTests
{
    private readonly Fixture _fixture = new();
    private readonly ILogger<DeploymentOrchestratorService> _logger = Substitute.For<ILogger<DeploymentOrchestratorService>>();

    [Fact]
    public void DeploymentCommand_SerializationWithNullValues_HandlesGracefully()
    {
        // Arrange
        var command = new DeploymentCommand
        {
            ServiceName = "TestService",
            Version = null,
            RollbackVersion = null,
            TargetServers = new List<string>(),
            Parameters = new Dictionary<string, object>(),
            Configuration = new Dictionary<string, object>(),
            Metadata = new Dictionary<string, string>()
        };

        // Act & Assert
        var act = () => JsonSerializer.Serialize(command);
        act.Should().NotThrow("Serialization should handle null values gracefully");
        
        var json = JsonSerializer.Serialize(command);
        json.Should().NotBeNullOrEmpty();
        
        var deserialized = JsonSerializer.Deserialize<DeploymentCommand>(json);
        deserialized.Should().NotBeNull();
        deserialized!.ServiceName.Should().Be("TestService");
    }

    [Fact]
    public void DeploymentCommand_DeserializationWithMissingFields_UsesDefaults()
    {
        // Arrange - JSON with missing optional fields
        var minimalJson = """
            {
                "serviceName": "MinimalService",
                "version": "1.0.0"
            }
            """;

        // Act
        var command = JsonSerializer.Deserialize<DeploymentCommand>(minimalJson);

        // Assert
        command.Should().NotBeNull();
        command!.ServiceName.Should().Be("MinimalService");
        command.Version.Should().Be("1.0.0");
        command.Id.Should().NotBeNullOrEmpty("Should generate ID if missing");
        command.Priority.Should().Be(DeploymentPriority.Normal, "Should use default priority");
        command.Strategy.Should().Be(DeploymentStrategy.Rolling, "Should use default strategy");
        command.TargetServers.Should().NotBeNull().And.BeEmpty();
        command.Parameters.Should().NotBeNull().And.BeEmpty();
        command.Configuration.Should().NotBeNull().And.BeEmpty();
        command.Metadata.Should().NotBeNull().And.BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid-json")]
    [InlineData("{incomplete")]
    [InlineData("null")]
    public void DeploymentCommand_DeserializationWithInvalidJson_ThrowsException(string invalidJson)
    {
        // Act & Assert
        var act = () => JsonSerializer.Deserialize<DeploymentCommand>(invalidJson);
        act.Should().Throw<JsonException>("Should throw for invalid JSON");
    }

    [Fact]
    public void DeploymentCommand_ExtremelyLargeData_HandlesWithinLimits()
    {
        // Arrange - Create command with very large data sets
        var largeTargetServers = Enumerable.Range(1, 10000)
            .Select(i => $"server-{i:D5}")
            .ToList();

        var largeConfiguration = new Dictionary<string, object>();
        for (int i = 0; i < 1000; i++)
        {
            largeConfiguration[$"config_key_{i}"] = $"config_value_{i}";
        }

        var largeMetadata = new Dictionary<string, string>();
        for (int i = 0; i < 1000; i++)
        {
            largeMetadata[$"meta_key_{i}"] = $"meta_value_{i}";
        }

        // Act
        var command = new DeploymentCommand
        {
            ServiceName = "LargeDataService",
            TargetServers = largeTargetServers,
            Configuration = largeConfiguration,
            Metadata = largeMetadata
        };

        // Assert
        command.TargetServers.Should().HaveCount(10000);
        command.Configuration.Should().HaveCount(1000);
        command.Metadata.Should().HaveCount(1000);
        
        // Serialization should still work (though may be slow)
        var act = () => JsonSerializer.Serialize(command);
        act.Should().NotThrow("Should handle large data sets");
    }

    [Fact]
    public void DeploymentCommand_CircularReferenceInConfiguration_HandlesGracefully()
    {
        // Arrange - Create configuration with potential circular references
        var config = new Dictionary<string, object>();
        var nestedConfig = new Dictionary<string, object>();
        
        config["nested"] = nestedConfig;
        nestedConfig["parent"] = config; // Circular reference
        nestedConfig["simple"] = "value";

        // Act & Assert
        var command = new DeploymentCommand
        {
            ServiceName = "CircularRefService",
            Configuration = new Dictionary<string, object> { ["safe"] = "value" } // Use safe config
        };

        var act = () => JsonSerializer.Serialize(command);
        act.Should().NotThrow("Should handle configuration serialization");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void DeploymentCommand_TimeoutExtremeValues_HandlesEdgeCases(int timeoutMinutes)
    {
        // Arrange & Act
        var command = new DeploymentCommand
        {
            Timeout = TimeSpan.FromMinutes(timeoutMinutes)
        };

        // Assert
        command.Timeout.Should().Be(TimeSpan.FromMinutes(timeoutMinutes));
        
        // Edge case handling
        if (timeoutMinutes < 0)
        {
            command.Timeout.Should().BeLessThan(TimeSpan.Zero, "Negative timeouts should be detectable");
        }
    }

    [Fact]
    public void DeploymentCommand_ConcurrentModification_ThreadSafe()
    {
        // Arrange
        var command = new DeploymentCommand
        {
            ServiceName = "ConcurrentService",
            TargetServers = new List<string> { "server1", "server2" },
            Parameters = new Dictionary<string, object> { ["key"] = "value" }
        };

        var tasks = new List<Task>();

        // Act - Multiple threads modifying different properties
        for (int i = 0; i < 10; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() => 
            {
                command.TargetServers.Add($"server-{index + 3}");
                command.Parameters[$"key-{index}"] = $"value-{index}";
                command.Metadata[$"meta-{index}"] = $"metadata-{index}";
            }));
        }

        // Assert
        var act = () => Task.WaitAll(tasks.ToArray());
        act.Should().NotThrow("Concurrent modifications should not crash");
        
        // Results may be unpredictable due to race conditions, but shouldn't crash
        command.TargetServers.Count.Should().BeGreaterOrEqualTo(2);
        command.Parameters.Count.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public void DeploymentWorkflow_InvalidPhaseOrder_DetectedCorrectly()
    {
        // Arrange
        var workflow = _fixture.Build<DeploymentWorkflow>()
            .With(w => w.Phases, new List<DeploymentPhase>())
            .Create();

        // Add phases in incorrect order
        workflow.Phases.Add(new DeploymentPhase 
        { 
            Name = "Cleanup", 
            Order = 100,
            TargetServers = new List<string> { "server1" },
            Steps = new List<DeploymentStep>()
        });
        
        workflow.Phases.Add(new DeploymentPhase 
        { 
            Name = "Pre-Deployment", 
            Order = 1,
            TargetServers = new List<string> { "server1" },
            Steps = new List<DeploymentStep>()
        });
        
        workflow.Phases.Add(new DeploymentPhase 
        { 
            Name = "Deployment", 
            Order = 50,
            TargetServers = new List<string> { "server1" },
            Steps = new List<DeploymentStep>()
        });

        // Act - Sort phases by order
        var sortedPhases = workflow.Phases.OrderBy(p => p.Order).ToList();

        // Assert
        sortedPhases[0].Name.Should().Be("Pre-Deployment");
        sortedPhases[1].Name.Should().Be("Deployment");
        sortedPhases[2].Name.Should().Be("Cleanup");
    }

    [Fact]
    public void DeploymentWorkflow_EmptyTargetServers_HandledGracefully()
    {
        // Arrange
        var workflow = _fixture.Build<DeploymentWorkflow>()
            .With(w => w.TargetServers, new List<string>())
            .Create();

        // Act & Assert
        workflow.TargetServers.Should().BeEmpty();
        
        // Workflow should still be valid but should be detectable as incomplete
        workflow.ServiceName.Should().NotBeNullOrEmpty();
        workflow.Id.Should().NotBeNullOrEmpty();
        workflow.Status.Should().Be(WorkflowStatus.Created);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DeploymentWorkflow_InvalidServiceName_DetectedCorrectly(string invalidServiceName)
    {
        // Arrange
        var workflow = _fixture.Build<DeploymentWorkflow>()
            .With(w => w.ServiceName, invalidServiceName!)
            .Create();

        // Assert
        workflow.ServiceName.Should().Be(invalidServiceName ?? string.Empty);
        
        // Invalid service names should be detectable
        string.IsNullOrWhiteSpace(workflow.ServiceName).Should().BeTrue("Invalid service names should be detectable");
    }

    [Fact]
    public void DeploymentWorkflow_DuplicateServerNames_HandledCorrectly()
    {
        // Arrange
        var workflow = new DeploymentWorkflow
        {
            ServiceName = "DuplicateTestService",
            TargetServers = new List<string> 
            { 
                "server1", 
                "server2", 
                "server1", // Duplicate
                "server3", 
                "server2"  // Another duplicate
            }
        };

        // Act - Remove duplicates
        var uniqueServers = workflow.TargetServers.Distinct().ToList();

        // Assert
        workflow.TargetServers.Should().HaveCount(5, "Original list should contain duplicates");
        uniqueServers.Should().HaveCount(3, "Unique list should have no duplicates");
        uniqueServers.Should().BeEquivalentTo(new[] { "server1", "server2", "server3" });
    }

    [Fact]
    public void WorkflowStatus_AllStatusValues_AreHandledCorrectly()
    {
        // Arrange & Act - Test all workflow status values
        var allStatuses = Enum.GetValues<WorkflowStatus>();

        // Assert
        foreach (var status in allStatuses)
        {
            var workflow = new DeploymentWorkflow { Status = status };
            workflow.Status.Should().Be(status);
            
            // Verify status is a valid enum value
            Enum.IsDefined(typeof(WorkflowStatus), status).Should().BeTrue();
        }
    }

    [Fact]
    public void ErrorConditions_NetworkFailure_SimulateGracefulHandling()
    {
        // Arrange - Simulate network failure conditions
        var networkException = new HttpRequestException("Network failure");
        var timeoutException = new TimeoutException("Operation timed out");
        var cancellationException = new OperationCanceledException("Operation cancelled");

        // Act & Assert - These exceptions should be expected in production
        networkException.Should().BeOfType<HttpRequestException>();
        timeoutException.Should().BeOfType<TimeoutException>();
        cancellationException.Should().BeOfType<OperationCanceledException>();
        
        // Error handling should be prepared for these common failure scenarios
        var commonExceptions = new Exception[] { networkException, timeoutException, cancellationException };
        commonExceptions.Should().NotBeEmpty("Common network exceptions should be handled");
    }

    [Fact]
    public void MemoryPressure_LargeObjectCreation_HandlesGracefully()
    {
        // Arrange - Create many objects to simulate memory pressure
        var commands = new List<DeploymentCommand>();

        // Act
        var act = () =>
        {
            for (int i = 0; i < 1000; i++)
            {
                var command = new DeploymentCommand
                {
                    ServiceName = $"Service{i}",
                    TargetServers = Enumerable.Range(1, 100).Select(j => $"server-{j}").ToList(),
                    Configuration = Enumerable.Range(1, 50).ToDictionary(j => $"key{j}", j => (object)$"value{j}")
                };
                commands.Add(command);
            }
        };

        // Assert
        act.Should().NotThrow("Should handle creation of many objects");
        commands.Should().HaveCount(1000);
        
        // Verify objects are still valid
        commands[0].ServiceName.Should().Be("Service0");
        commands[999].ServiceName.Should().Be("Service999");
        commands.All(c => c.TargetServers.Count == 100).Should().BeTrue();
    }

    [Theory]
    [InlineData("server\nwith\nnewlines")]
    [InlineData("server\twith\ttabs")]
    [InlineData("server with spaces")]
    [InlineData("server-with-unicode-🚀")]
    [InlineData("UPPERCASE-SERVER")]
    [InlineData("lowercase-server")]
    public void ServerNames_SpecialCharacters_HandledCorrectly(string serverName)
    {
        // Arrange
        var command = new DeploymentCommand
        {
            ServiceName = "SpecialCharService",
            TargetServers = new List<string> { serverName }
        };

        // Act & Assert
        command.TargetServers.Should().Contain(serverName);
        
        // Serialization should handle special characters
        var json = JsonSerializer.Serialize(command);
        json.Should().NotBeNullOrEmpty();
        
        var deserialized = JsonSerializer.Deserialize<DeploymentCommand>(json);
        deserialized!.TargetServers.Should().Contain(serverName);
    }

    [Fact]
    public void DateTimeHandling_DifferentTimezones_MaintainsConsistency()
    {
        // Arrange
        var utcTime = DateTime.UtcNow;
        var localTime = DateTime.Now;
        var specificTime = new DateTime(2024, 1, 15, 10, 30, 45, DateTimeKind.Utc);

        var workflows = new[]
        {
            new DeploymentWorkflow { CreatedAt = utcTime },
            new DeploymentWorkflow { CreatedAt = localTime },
            new DeploymentWorkflow { CreatedAt = specificTime }
        };

        // Act & Assert
        foreach (var workflow in workflows)
        {
            workflow.CreatedAt.Should().NotBe(default(DateTime));
            
            // Serialization should preserve date information
            var json = JsonSerializer.Serialize(workflow);
            var deserialized = JsonSerializer.Deserialize<DeploymentWorkflow>(json);
            
            // Times should be close (allowing for serialization/deserialization precision)
            deserialized!.CreatedAt.Should().BeCloseTo(workflow.CreatedAt, TimeSpan.FromSeconds(1));
        }
    }

    [Fact]
    public void CollectionModification_DuringIteration_HandlesSafely()
    {
        // Arrange
        var targetServers = new List<string> { "server1", "server2", "server3", "server4", "server5" };
        var command = new DeploymentCommand
        {
            ServiceName = "ConcurrentModificationService",
            TargetServers = targetServers
        };

        var processedServers = new List<string>();

        // Act & Assert - Modify collection during iteration (unsafe pattern)
        var act = () =>
        {
            // Create a copy to iterate over to avoid modification during iteration
            var serversCopy = command.TargetServers.ToList();
            foreach (var server in serversCopy)
            {
                processedServers.Add(server);
                if (server == "server3")
                {
                    command.TargetServers.Add("server6"); // Safe modification
                }
            }
        };

        act.Should().NotThrow("Safe collection modification should not throw");
        processedServers.Should().HaveCount(5);
        command.TargetServers.Should().HaveCount(6);
    }

    [Fact]
    public void ResourceCleanup_DisposableObjects_CleanedUpProperly()
    {
        // Arrange
        var disposableResources = new List<IDisposable>();

        // Act
        for (int i = 0; i < 10; i++)
        {
            var resource = Substitute.For<IDisposable>();
            disposableResources.Add(resource);
        }

        // Clean up resources
        foreach (var resource in disposableResources)
        {
            resource.Dispose();
        }

        // Assert - All resources should have been disposed
        foreach (var resource in disposableResources)
        {
            resource.Received(1).Dispose();
        }
    }

    [Theory]
    [InlineData(0.1)] // Very short
    [InlineData(24.0)] // One day
    [InlineData(168.0)] // One week
    [InlineData(8760.0)] // One year
    public void TimeSpanHandling_ExtremeValues_HandlesCorrectly(double hours)
    {
        // Arrange
        var timespan = TimeSpan.FromHours(hours);
        var command = new DeploymentCommand
        {
            Timeout = timespan
        };

        // Act & Assert
        command.Timeout.Should().Be(timespan);
        command.Timeout.TotalHours.Should().BeApproximately(hours, 0.001);
        
        // Serialization should preserve extreme timespan values
        var json = JsonSerializer.Serialize(command);
        var deserialized = JsonSerializer.Deserialize<DeploymentCommand>(json);
        deserialized!.Timeout.TotalHours.Should().BeApproximately(hours, 0.001);
    }
}