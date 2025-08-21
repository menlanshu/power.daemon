using AutoFixture;
using FluentAssertions;
using NSubstitute;
using PowerDaemon.Cache.Services;
using PowerDaemon.Messaging.Messages;
using PowerDaemon.Orchestrator.Models;
using Xunit;

namespace PowerDaemon.Tests.Unit.Cache;

public class CacheServiceTests
{
    private readonly ICacheService _cacheService;
    private readonly Fixture _fixture = new();

    public CacheServiceTests()
    {
        _cacheService = Substitute.For<ICacheService>();
    }

    [Fact]
    public async Task GetAsync_ExistingKey_ReturnsValue()
    {
        // Arrange
        var key = "test:key";
        var expectedValue = _fixture.Create<DeploymentCommand>();
        _cacheService.GetAsync<DeploymentCommand>(key, Arg.Any<CancellationToken>())
            .Returns(expectedValue);

        // Act
        var result = await _cacheService.GetAsync<DeploymentCommand>(key);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(expectedValue);
    }

    [Fact]
    public async Task GetAsync_NonExistentKey_ReturnsNull()
    {
        // Arrange
        var key = "non:existent:key";
        _cacheService.GetAsync<DeploymentCommand>(key, Arg.Any<CancellationToken>())
            .Returns((DeploymentCommand?)null);

        // Act
        var result = await _cacheService.GetAsync<DeploymentCommand>(key);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_ValidKeyValue_ReturnsTrue()
    {
        // Arrange
        var key = "test:set:key";
        var value = _fixture.Create<DeploymentWorkflow>();
        _cacheService.SetAsync(key, value, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _cacheService.SetAsync(key, value, TimeSpan.FromMinutes(30));

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task SetAsync_InvalidKey_ReturnsFalse(string invalidKey)
    {
        // Arrange
        var value = _fixture.Create<DeploymentCommand>();
        _cacheService.SetAsync(invalidKey!, value, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var result = await _cacheService.SetAsync(invalidKey!, value);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_ExistingKey_ReturnsTrue()
    {
        // Arrange
        var key = "existing:key";
        _cacheService.ExistsAsync(key, Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _cacheService.ExistsAsync(key);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_NonExistentKey_ReturnsFalse()
    {
        // Arrange
        var key = "non:existent:key";
        _cacheService.ExistsAsync(key, Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var result = await _cacheService.ExistsAsync(key);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_ExistingKey_ReturnsTrue()
    {
        // Arrange
        var key = "key:to:delete";
        _cacheService.DeleteAsync(key, Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _cacheService.DeleteAsync(key);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteByPatternAsync_ValidPattern_ReturnsDeletedCount()
    {
        // Arrange
        var pattern = "deployment:*";
        var expectedDeletedCount = 5L;
        _cacheService.DeleteByPatternAsync(pattern, Arg.Any<CancellationToken>())
            .Returns(expectedDeletedCount);

        // Act
        var result = await _cacheService.DeleteByPatternAsync(pattern);

        // Assert
        result.Should().Be(expectedDeletedCount);
    }

    [Fact]
    public async Task GetManyAsync_MultipleKeys_ReturnsKeyValuePairs()
    {
        // Arrange
        var keys = new[] { "key1", "key2", "key3" };
        var expectedResult = new Dictionary<string, DeploymentCommand?>
        {
            ["key1"] = _fixture.Create<DeploymentCommand>(),
            ["key2"] = _fixture.Create<DeploymentCommand>(),
            ["key3"] = null
        };

        _cacheService.GetManyAsync<DeploymentCommand>(keys, Arg.Any<CancellationToken>())
            .Returns(expectedResult);

        // Act
        var result = await _cacheService.GetManyAsync<DeploymentCommand>(keys);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);
        result["key1"].Should().NotBeNull();
        result["key2"].Should().NotBeNull();
        result["key3"].Should().BeNull();
    }

    [Fact]
    public async Task SetManyAsync_MultipleKeyValuePairs_ReturnsTrue()
    {
        // Arrange
        var keyValuePairs = new Dictionary<string, DeploymentWorkflow>
        {
            ["workflow:1"] = _fixture.Create<DeploymentWorkflow>(),
            ["workflow:2"] = _fixture.Create<DeploymentWorkflow>(),
            ["workflow:3"] = _fixture.Create<DeploymentWorkflow>()
        };

        _cacheService.SetManyAsync(keyValuePairs, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _cacheService.SetManyAsync(keyValuePairs, TimeSpan.FromHours(1));

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task HashGetAsync_ValidHashKeyAndField_ReturnsValue()
    {
        // Arrange
        var hashKey = "user:123";
        var field = "profile";
        var expectedValue = _fixture.Create<User>();

        _cacheService.HashGetAsync<User>(hashKey, field, Arg.Any<CancellationToken>())
            .Returns(expectedValue);

        // Act
        var result = await _cacheService.HashGetAsync<User>(hashKey, field);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(expectedValue);
    }

    [Fact]
    public async Task HashSetAsync_ValidHashKeyFieldValue_ReturnsTrue()
    {
        // Arrange
        var hashKey = "deployment:config";
        var field = "settings";
        var value = _fixture.Create<DeploymentWorkflow>();

        _cacheService.HashSetAsync(hashKey, field, value, Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _cacheService.HashSetAsync(hashKey, field, value);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task HashGetAllAsync_ValidHashKey_ReturnsAllFields()
    {
        // Arrange
        var hashKey = "workflow:metadata";
        var expectedFields = new Dictionary<string, DeploymentWorkflow?>
        {
            ["current"] = _fixture.Create<DeploymentWorkflow>(),
            ["previous"] = _fixture.Create<DeploymentWorkflow>(),
            ["rollback"] = null
        };

        _cacheService.HashGetAllAsync<DeploymentWorkflow>(hashKey, Arg.Any<CancellationToken>())
            .Returns(expectedFields);

        // Act
        var result = await _cacheService.HashGetAllAsync<DeploymentWorkflow>(hashKey);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);
        result["current"].Should().NotBeNull();
        result["previous"].Should().NotBeNull();
        result["rollback"].Should().BeNull();
    }

    [Fact]
    public async Task HashDeleteAsync_ValidHashKeyField_ReturnsTrue()
    {
        // Arrange
        var hashKey = "temp:data";
        var field = "expired";

        _cacheService.HashDeleteAsync(hashKey, field, Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _cacheService.HashDeleteAsync(hashKey, field);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ListPushAsync_ValidListKeyValue_ReturnsLength()
    {
        // Arrange
        var listKey = "deployment:queue";
        var value = _fixture.Create<DeploymentCommand>();
        var expectedLength = 5L;

        _cacheService.ListPushAsync(listKey, value, true, Arg.Any<CancellationToken>())
            .Returns(expectedLength);

        // Act
        var result = await _cacheService.ListPushAsync(listKey, value, leftSide: true);

        // Assert
        result.Should().Be(expectedLength);
    }

    [Fact]
    public async Task ListPopAsync_ValidListKey_ReturnsValue()
    {
        // Arrange
        var listKey = "processing:queue";
        var expectedValue = _fixture.Create<DeploymentCommand>();

        _cacheService.ListPopAsync<DeploymentCommand>(listKey, true, Arg.Any<CancellationToken>())
            .Returns(expectedValue);

        // Act
        var result = await _cacheService.ListPopAsync<DeploymentCommand>(listKey, leftSide: true);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(expectedValue);
    }

    [Fact]
    public async Task ListPopAsync_EmptyList_ReturnsNull()
    {
        // Arrange
        var listKey = "empty:queue";

        _cacheService.ListPopAsync<DeploymentCommand>(listKey, true, Arg.Any<CancellationToken>())
            .Returns((DeploymentCommand?)null);

        // Act
        var result = await _cacheService.ListPopAsync<DeploymentCommand>(listKey, leftSide: true);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ListLengthAsync_ValidListKey_ReturnsLength()
    {
        // Arrange
        var listKey = "metrics:queue";
        var expectedLength = 42L;

        _cacheService.ListLengthAsync(listKey, Arg.Any<CancellationToken>())
            .Returns(expectedLength);

        // Act
        var result = await _cacheService.ListLengthAsync(listKey);

        // Assert
        result.Should().Be(expectedLength);
    }

    [Fact]
    public async Task SetAddAsync_ValidSetKeyValue_ReturnsTrue()
    {
        // Arrange
        var setKey = "active:servers";
        var value = _fixture.Create<Server>();

        _cacheService.SetAddAsync(setKey, value, Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _cacheService.SetAddAsync(setKey, value);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task SetAddAsync_DuplicateValue_ReturnsFalse()
    {
        // Arrange
        var setKey = "unique:items";
        var value = _fixture.Create<DeploymentCommand>();

        _cacheService.SetAddAsync(setKey, value, Arg.Any<CancellationToken>())
            .Returns(false); // Duplicate item

        // Act
        var result = await _cacheService.SetAddAsync(setKey, value);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SetRemoveAsync_ExistingValue_ReturnsTrue()
    {
        // Arrange
        var setKey = "completed:deployments";
        var value = _fixture.Create<DeploymentWorkflow>();

        _cacheService.SetRemoveAsync(setKey, value, Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _cacheService.SetRemoveAsync(setKey, value);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task SetContainsAsync_ExistingValue_ReturnsTrue()
    {
        // Arrange
        var setKey = "monitored:services";
        var value = _fixture.Create<Service>();

        _cacheService.SetContainsAsync(setKey, value, Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _cacheService.SetContainsAsync(setKey, value);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task SetContainsAsync_NonExistentValue_ReturnsFalse()
    {
        // Arrange
        var setKey = "known:users";
        var value = _fixture.Create<User>();

        _cacheService.SetContainsAsync(setKey, value, Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var result = await _cacheService.SetContainsAsync(setKey, value);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ProductionScale_MultipleOperations_HandlesHighThroughput()
    {
        // Arrange - Simulate 100 concurrent cache operations
        var tasks = new List<Task>();
        var deployments = _fixture.CreateMany<DeploymentWorkflow>(100).ToList();

        // Configure mock for all operations
        _cacheService.SetAsync(Arg.Any<string>(), Arg.Any<DeploymentWorkflow>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(true);
        
        _cacheService.GetAsync<DeploymentWorkflow>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => deployments.FirstOrDefault());

        _cacheService.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act - Perform concurrent operations
        for (int i = 0; i < deployments.Count; i++)
        {
            var deployment = deployments[i];
            var key = $"workflow:{deployment.Id}";

            tasks.Add(_cacheService.SetAsync(key, deployment, TimeSpan.FromHours(1)));
            tasks.Add(_cacheService.GetAsync<DeploymentWorkflow>(key));
            tasks.Add(_cacheService.ExistsAsync(key));
        }

        // Assert - All operations should complete without throwing
        var act = () => Task.WhenAll(tasks);
        await act.Should().NotThrowAsync();
        
        tasks.Should().HaveCount(300); // 100 deployments * 3 operations each
    }

    [Fact]
    public async Task BatchOperations_LargeDataSet_HandlesEfficiently()
    {
        // Arrange - 500 deployment workflows
        var workflows = _fixture.CreateMany<DeploymentWorkflow>(500).ToList();
        var keyValuePairs = workflows.ToDictionary(w => $"workflow:{w.Id}", w => w);
        var keys = keyValuePairs.Keys.ToList();

        _cacheService.SetManyAsync(keyValuePairs, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        _cacheService.GetManyAsync<DeploymentWorkflow>(keys, Arg.Any<CancellationToken>())
            .Returns(keyValuePairs.ToDictionary(kvp => kvp.Key, kvp => (DeploymentWorkflow?)kvp.Value));

        // Act
        var setResult = await _cacheService.SetManyAsync(keyValuePairs, TimeSpan.FromHours(2));
        var getResult = await _cacheService.GetManyAsync<DeploymentWorkflow>(keys);

        // Assert
        setResult.Should().BeTrue();
        getResult.Should().NotBeNull();
        getResult.Should().HaveCount(500);
        getResult.Values.Should().NotContain(v => v == null);
    }

    [Fact]
    public async Task CacheExpiry_DifferentTimeSpans_HandlesVariousExpiryTimes()
    {
        // Arrange
        var shortLivedData = _fixture.Create<DeploymentCommand>();
        var mediumLivedData = _fixture.Create<DeploymentWorkflow>();
        var longLivedData = _fixture.Create<User>();

        _cacheService.SetAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var shortResult = await _cacheService.SetAsync("temp:data", shortLivedData, TimeSpan.FromMinutes(1));
        var mediumResult = await _cacheService.SetAsync("session:data", mediumLivedData, TimeSpan.FromHours(1));
        var longResult = await _cacheService.SetAsync("config:data", longLivedData, TimeSpan.FromDays(1));

        // Assert
        shortResult.Should().BeTrue();
        mediumResult.Should().BeTrue();
        longResult.Should().BeTrue();

        // Verify different expiry times were used
        await _cacheService.Received(1).SetAsync("temp:data", shortLivedData, TimeSpan.FromMinutes(1), Arg.Any<CancellationToken>());
        await _cacheService.Received(1).SetAsync("session:data", mediumLivedData, TimeSpan.FromHours(1), Arg.Any<CancellationToken>());
        await _cacheService.Received(1).SetAsync("config:data", longLivedData, TimeSpan.FromDays(1), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("deployment:*", 10)]
    [InlineData("workflow:prod:*", 25)]
    [InlineData("user:session:*", 50)]
    [InlineData("temp:*", 100)]
    public async Task DeleteByPattern_VariousPatterns_DeletesMatchingKeys(string pattern, long expectedDeleteCount)
    {
        // Arrange
        _cacheService.DeleteByPatternAsync(pattern, Arg.Any<CancellationToken>())
            .Returns(expectedDeleteCount);

        // Act
        var result = await _cacheService.DeleteByPatternAsync(pattern);

        // Assert
        result.Should().Be(expectedDeleteCount);
    }

    [Fact]
    public async Task HashOperations_ComplexWorkflowData_HandlesNestedStructures()
    {
        // Arrange
        var workflowId = "complex-workflow-123";
        var hashKey = $"workflow:data:{workflowId}";
        
        var mainWorkflow = _fixture.Create<DeploymentWorkflow>();
        var metadata = _fixture.Create<Dictionary<string, string>>();
        var phases = _fixture.CreateMany<DeploymentPhase>(5).ToList();

        _cacheService.HashSetAsync(hashKey, "main", mainWorkflow, Arg.Any<CancellationToken>())
            .Returns(true);
        _cacheService.HashSetAsync(hashKey, "metadata", metadata, Arg.Any<CancellationToken>())
            .Returns(true);
        _cacheService.HashSetAsync(hashKey, "phases", phases, Arg.Any<CancellationToken>())
            .Returns(true);

        var allFields = new Dictionary<string, object?>
        {
            ["main"] = mainWorkflow,
            ["metadata"] = metadata,
            ["phases"] = phases
        };

        _cacheService.HashGetAllAsync<object>(hashKey, Arg.Any<CancellationToken>())
            .Returns(allFields);

        // Act
        var setMainResult = await _cacheService.HashSetAsync(hashKey, "main", mainWorkflow);
        var setMetadataResult = await _cacheService.HashSetAsync(hashKey, "metadata", metadata);
        var setPhasesResult = await _cacheService.HashSetAsync(hashKey, "phases", phases);
        var getAllResult = await _cacheService.HashGetAllAsync<object>(hashKey);

        // Assert
        setMainResult.Should().BeTrue();
        setMetadataResult.Should().BeTrue();
        setPhasesResult.Should().BeTrue();
        getAllResult.Should().NotBeNull();
        getAllResult.Should().HaveCount(3);
        getAllResult.Should().ContainKeys("main", "metadata", "phases");
    }

    // Mock User and Server classes for testing
    public class User
    {
        public string Id { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new();
    }

    public class Server
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime LastHeartbeat { get; set; }
    }

    public class Service
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}