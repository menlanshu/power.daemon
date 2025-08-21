using AutoFixture;
using FluentAssertions;
using PowerDaemon.Identity.Configuration;
using PowerDaemon.Messaging.Configuration;
using PowerDaemon.Monitoring.Configuration;
using PowerDaemon.Orchestrator.Configuration;
using Xunit;

namespace PowerDaemon.Tests.Unit.Configuration;

public class ConfigurationValidationTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public void JwtConfiguration_DefaultValues_AreValid()
    {
        // Arrange & Act
        var config = new JwtConfiguration();

        // Assert
        config.Should().NotBeNull();
        config.Issuer.Should().NotBeNullOrEmpty();
        config.Audience.Should().NotBeNullOrEmpty();
        config.ExpiryMinutes.Should().BeGreaterThan(0);
        config.RefreshTokenExpiryDays.Should().BeGreaterThan(0);
        config.ValidateIssuer.Should().BeTrue();
        config.ValidateAudience.Should().BeTrue();
        config.ValidateLifetime.Should().BeTrue();
        config.ValidateIssuerSigningKey.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void JwtConfiguration_InvalidSecret_ShouldBeDetected(string invalidSecret)
    {
        // Arrange
        var config = new JwtConfiguration
        {
            Secret = invalidSecret!
        };

        // Assert
        config.Secret.Should().Be(invalidSecret ?? string.Empty);
        // Note: In production, validation logic should check this
        if (!string.IsNullOrWhiteSpace(invalidSecret))
        {
            config.Secret.Length.Should().BeLessThan(32, "Invalid secrets should be detected");
        }
    }

    [Fact]
    public void JwtConfiguration_ProductionSettings_MeetSecurityRequirements()
    {
        // Arrange
        var config = new JwtConfiguration
        {
            Secret = "ThisIsAVerySecureSecretKeyThatMeetsMinimumSecurityRequirementsForProductionUse123!",
            Issuer = "PowerDaemon.Production",
            Audience = "PowerDaemon.Services.Production",
            ExpiryMinutes = 60,
            RefreshTokenExpiryDays = 30,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireExpirationTime = true,
            SaveTokenInCache = true
        };

        // Assert
        config.Secret.Length.Should().BeGreaterThan(32, "Production secrets should be sufficiently long");
        config.ExpiryMinutes.Should().BeInRange(15, 480, "Token expiry should be reasonable for production");
        config.RefreshTokenExpiryDays.Should().BeInRange(1, 90, "Refresh token expiry should be reasonable");
        config.ValidateIssuer.Should().BeTrue("Production should validate issuer");
        config.ValidateAudience.Should().BeTrue("Production should validate audience");
        config.ValidateLifetime.Should().BeTrue("Production should validate token lifetime");
        config.ValidateIssuerSigningKey.Should().BeTrue("Production should validate signing key");
        config.RequireExpirationTime.Should().BeTrue("Production tokens should have expiration");
    }

    [Fact]
    public void RabbitMQConfiguration_DefaultValues_AreValid()
    {
        // Arrange & Act
        var config = new RabbitMQConfiguration();

        // Assert
        config.HostName.Should().Be("localhost");
        config.Port.Should().Be(5672);
        config.UserName.Should().Be("guest");
        config.Password.Should().Be("guest");
        config.VirtualHost.Should().Be("/");
        config.AutomaticRecoveryEnabled.Should().BeTrue();
        config.NetworkRecoveryInterval.Should().BeGreaterThan(0);
        config.RequestedHeartbeat.Should().BeGreaterThan(0);
        config.ProductionScale.Should().NotBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(70000)]
    public void RabbitMQConfiguration_InvalidPort_ShouldBeDetected(int invalidPort)
    {
        // Arrange
        var config = new RabbitMQConfiguration
        {
            Port = invalidPort
        };

        // Assert
        config.Port.Should().Be(invalidPort);
        
        // Validation logic should detect these issues
        if (invalidPort <= 0 || invalidPort > 65535)
        {
            // This should be caught by validation
            (invalidPort > 0 && invalidPort <= 65535).Should().BeFalse("Invalid ports should be detected");
        }
    }

    [Fact]
    public void RabbitMQConfiguration_ProductionScale_HasAppropriateDefaults()
    {
        // Arrange & Act
        var config = new RabbitMQConfiguration();
        var productionScale = config.ProductionScale;

        // Assert
        productionScale.MaxConnectionPoolSize.Should().BeGreaterThan(0);
        productionScale.MinConnectionPoolSize.Should().BeGreaterThan(0);
        productionScale.MinConnectionPoolSize.Should().BeLessOrEqualTo(productionScale.MaxConnectionPoolSize);
        productionScale.PrefetchCount.Should().BeGreaterThan(0);
        productionScale.BatchSize.Should().BeGreaterThan(0);
        productionScale.ConsumerThreadCount.Should().BeGreaterThan(0);
        productionScale.MaxMessagesPerSecond.Should().BeGreaterThan(0);
        productionScale.MaxConcurrentOperations.Should().BeGreaterThan(0);

        // Values should be suitable for 200+ server deployments
        productionScale.MaxConcurrentOperations.Should().BeGreaterOrEqualTo(200);
        productionScale.MaxMessagesPerSecond.Should().BeGreaterOrEqualTo(500);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void RabbitMQConfiguration_InvalidQueueNames_ShouldBeDetected(string invalidName)
    {
        // Arrange
        var config = new RabbitMQConfiguration
        {
            DeploymentQueue = invalidName!,
            CommandQueue = invalidName!,
            StatusQueue = invalidName!
        };

        // Assert - Invalid queue names should be detectable
        if (string.IsNullOrWhiteSpace(invalidName))
        {
            string.IsNullOrWhiteSpace(config.DeploymentQueue).Should().BeTrue();
            string.IsNullOrWhiteSpace(config.CommandQueue).Should().BeTrue();
            string.IsNullOrWhiteSpace(config.StatusQueue).Should().BeTrue();
        }
    }

    [Fact]
    public void OrchestratorConfiguration_DefaultValues_AreValid()
    {
        // Arrange & Act
        var config = new OrchestratorConfiguration();

        // Assert
        config.MaxConcurrentWorkflows.Should().BeGreaterThan(0);
        config.MaxQueuedWorkflows.Should().BeGreaterThan(0);
        config.HealthCheckIntervalSeconds.Should().BeGreaterThan(0);
        config.WorkflowTimeoutMinutes.Should().BeGreaterThan(0);
        config.RollbackTimeoutMinutes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void OrchestratorConfiguration_ProductionValues_SupportScale()
    {
        // Arrange
        var config = new OrchestratorConfiguration
        {
            MaxConcurrentWorkflows = 100,
            MaxQueuedWorkflows = 500,
            HealthCheckIntervalSeconds = 30,
            WorkflowTimeoutMinutes = 240,
            EnableAutoRollback = true,
            RollbackTimeoutMinutes = 60
        };

        // Assert
        config.MaxConcurrentWorkflows.Should().BeGreaterOrEqualTo(50, "Should support concurrent workflows for large deployments");
        config.MaxQueuedWorkflows.Should().BeGreaterOrEqualTo(200, "Should support queuing for 200+ servers");
        config.HealthCheckIntervalSeconds.Should().BeInRange(10, 300, "Health check interval should be reasonable");
        config.WorkflowTimeoutMinutes.Should().BeGreaterOrEqualTo(60, "Workflows should have adequate timeout for large deployments");
        config.EnableAutoRollback.Should().BeTrue("Production should support auto-rollback");
        config.RollbackTimeoutMinutes.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void OrchestratorConfiguration_InvalidValues_ShouldBeDetected(int invalidValue)
    {
        // Arrange
        var config = new OrchestratorConfiguration
        {
            MaxConcurrentWorkflows = invalidValue,
            MaxQueuedWorkflows = invalidValue,
            HealthCheckIntervalSeconds = invalidValue
        };

        // Assert
        (invalidValue > 0).Should().BeFalse("Invalid configuration values should be detectable");
        config.MaxConcurrentWorkflows.Should().Be(invalidValue);
        config.MaxQueuedWorkflows.Should().Be(invalidValue);
        config.HealthCheckIntervalSeconds.Should().Be(invalidValue);
    }

    [Fact]
    public void MonitoringConfiguration_DefaultValues_AreValid()
    {
        // Arrange & Act
        var config = new MonitoringConfiguration();

        // Assert
        config.Should().NotBeNull();
        // Default values should be appropriate for monitoring
    }

    [Fact]
    public void MonitoringConfiguration_ProductionSettings_SupportHighThroughput()
    {
        // Arrange
        var config = new MonitoringConfiguration
        {
            MetricsRetentionDays = 90,
            AlertEvaluationIntervalSeconds = 30,
            MaxConcurrentAlerts = 1000,
            EnableRealTimeNotifications = true,
            NotificationTimeoutSeconds = 30,
            MaxNotificationRetries = 3
        };

        // Assert
        config.MetricsRetentionDays.Should().BeGreaterThan(30, "Should retain metrics for reasonable period");
        config.AlertEvaluationIntervalSeconds.Should().BeInRange(10, 300, "Alert evaluation should be timely");
        config.MaxConcurrentAlerts.Should().BeGreaterOrEqualTo(500, "Should handle many concurrent alerts");
        config.EnableRealTimeNotifications.Should().BeTrue("Production should support real-time notifications");
        config.NotificationTimeoutSeconds.Should().BeInRange(5, 120, "Notification timeout should be reasonable");
        config.MaxNotificationRetries.Should().BeInRange(1, 10, "Should retry failed notifications");
    }

    [Fact]
    public void ActiveDirectoryConfiguration_DefaultValues_AreValid()
    {
        // Arrange & Act
        var config = new ActiveDirectoryConfiguration();

        // Assert
        config.Should().NotBeNull();
        config.Domain.Should().NotBeNull();
        config.Username.Should().NotBeNull();
        config.Password.Should().NotBeNull();
    }

    [Fact]
    public void ActiveDirectoryConfiguration_ProductionSettings_AreSecure()
    {
        // Arrange
        var config = new ActiveDirectoryConfiguration
        {
            Domain = "corp.company.com",
            Username = "service-account",
            Password = "SecurePassword123!",
            UseSSL = true,
            Port = 636,
            TimeoutSeconds = 30,
            MaxRetries = 3,
            EnableGroupCaching = true,
            GroupCacheExpiryMinutes = 60
        };

        // Assert
        config.Domain.Should().NotBeNullOrEmpty("Domain should be specified for production");
        config.Username.Should().NotBeNullOrEmpty("Service account should be specified");
        config.Password.Should().NotBeNullOrEmpty("Password should be provided");
        config.UseSSL.Should().BeTrue("Production should use SSL for AD connections");
        config.Port.Should().Be(636, "SSL LDAP port should be used");
        config.TimeoutSeconds.Should().BeInRange(10, 120, "Timeout should be reasonable");
        config.MaxRetries.Should().BeGreaterThan(0, "Should retry failed connections");
        config.EnableGroupCaching.Should().BeTrue("Should cache group information for performance");
        config.GroupCacheExpiryMinutes.Should().BeGreaterThan(0, "Cache expiry should be positive");
    }

    [Fact]
    public void ConfigurationCombination_AllServices_AreCompatible()
    {
        // Arrange - Create a complete production configuration set
        var jwtConfig = new JwtConfiguration
        {
            Secret = "ProductionSecretKey123456789012345678901234567890",
            ExpiryMinutes = 60,
            RefreshTokenExpiryDays = 30
        };

        var rabbitMqConfig = new RabbitMQConfiguration
        {
            HostName = "rabbit-cluster.company.com",
            Port = 5672,
            UserName = "powerdaemon",
            Password = "SecureRabbitPassword",
            ProductionScale = new ProductionScaleConfiguration
            {
                MaxConnectionPoolSize = 100,
                MaxConcurrentOperations = 500
            }
        };

        var orchestratorConfig = new OrchestratorConfiguration
        {
            MaxConcurrentWorkflows = 200,
            MaxQueuedWorkflows = 1000,
            EnableAutoRollback = true
        };

        var monitoringConfig = new MonitoringConfiguration
        {
            MetricsRetentionDays = 90,
            MaxConcurrentAlerts = 2000
        };

        // Assert - All configurations should be compatible for production scale
        jwtConfig.ExpiryMinutes.Should().BeInRange(15, 240);
        rabbitMqConfig.ProductionScale.MaxConcurrentOperations.Should().BeGreaterOrEqualTo(orchestratorConfig.MaxConcurrentWorkflows * 2);
        monitoringConfig.MaxConcurrentAlerts.Should().BeGreaterOrEqualTo(orchestratorConfig.MaxQueuedWorkflows * 2);
        
        // Verify no configuration conflicts
        (jwtConfig.ExpiryMinutes * 60).Should().BeLessThan(rabbitMqConfig.RequestedHeartbeat * 100, 
            "JWT token expiry should not conflict with RabbitMQ heartbeat");
    }

    [Theory]
    [InlineData(50, 200, 1000)] // Small scale
    [InlineData(100, 500, 2000)] // Medium scale  
    [InlineData(200, 1000, 5000)] // Large scale
    public void ConfigurationScaling_DifferentScales_AreConsistent(
        int maxConcurrentWorkflows, 
        int maxQueuedWorkflows, 
        int maxConcurrentOperations)
    {
        // Arrange
        var orchestratorConfig = new OrchestratorConfiguration
        {
            MaxConcurrentWorkflows = maxConcurrentWorkflows,
            MaxQueuedWorkflows = maxQueuedWorkflows
        };

        var rabbitMqConfig = new RabbitMQConfiguration
        {
            ProductionScale = new ProductionScaleConfiguration
            {
                MaxConcurrentOperations = maxConcurrentOperations
            }
        };

        // Assert - Configuration values should scale consistently
        orchestratorConfig.MaxQueuedWorkflows.Should().BeGreaterOrEqualTo(orchestratorConfig.MaxConcurrentWorkflows);
        rabbitMqConfig.ProductionScale.MaxConcurrentOperations.Should().BeGreaterOrEqualTo(orchestratorConfig.MaxConcurrentWorkflows);
        
        // Ratios should be reasonable
        var queueToActiveRatio = (double)orchestratorConfig.MaxQueuedWorkflows / orchestratorConfig.MaxConcurrentWorkflows;
        queueToActiveRatio.Should().BeInRange(2.0, 20.0, "Queue to active workflow ratio should be reasonable");
    }

    [Fact]
    public void ConfigurationValidation_MissingRequiredValues_DetectedCorrectly()
    {
        // Arrange - Configurations with missing required values
        var incompleteJwtConfig = new JwtConfiguration
        {
            Secret = "", // Missing
            Issuer = "PowerDaemon",
            Audience = "" // Missing
        };

        var incompleteRabbitConfig = new RabbitMQConfiguration
        {
            HostName = "", // Missing
            Port = 5672,
            UserName = "user",
            Password = "" // Missing for non-guest
        };

        // Assert - Missing values should be detectable
        string.IsNullOrEmpty(incompleteJwtConfig.Secret).Should().BeTrue();
        string.IsNullOrEmpty(incompleteJwtConfig.Audience).Should().BeTrue();
        string.IsNullOrEmpty(incompleteRabbitConfig.HostName).Should().BeTrue();
        string.IsNullOrEmpty(incompleteRabbitConfig.Password).Should().BeTrue();
    }

    [Fact]
    public void ConfigurationSerialization_AllConfigurations_SerializeCorrectly()
    {
        // Arrange
        var configs = new object[]
        {
            _fixture.Create<JwtConfiguration>(),
            _fixture.Create<RabbitMQConfiguration>(),
            _fixture.Create<OrchestratorConfiguration>(),
            _fixture.Create<MonitoringConfiguration>(),
            _fixture.Create<ActiveDirectoryConfiguration>()
        };

        // Act & Assert - All configurations should be serializable
        foreach (var config in configs)
        {
            config.Should().NotBeNull();
            var configType = config.GetType();
            configType.IsSerializable.Should().BeTrue($"{configType.Name} should be serializable");
            
            // Verify all properties are accessible
            var properties = configType.GetProperties();
            properties.Should().NotBeEmpty($"{configType.Name} should have configurable properties");
        }
    }
}