using Microsoft.Extensions.Logging;
using PowerDaemon.Orchestrator.Models;
using PowerDaemon.Orchestrator.Services;
using PowerDaemon.Messaging.Messages;
using OrchestratorServiceCommand = PowerDaemon.Orchestrator.Models.ServiceCommand;

namespace PowerDaemon.Orchestrator.Strategies;

public class CanaryDeploymentStrategy : IDeploymentStrategy
{
    private readonly ILogger<CanaryDeploymentStrategy> _logger;

    public DeploymentStrategy StrategyType => DeploymentStrategy.Canary;

    public CanaryDeploymentStrategy(ILogger<CanaryDeploymentStrategy> logger)
    {
        _logger = logger;
    }

    public async Task<List<DeploymentPhase>> CreatePhasesAsync(
        DeploymentWorkflowRequest request, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Creating canary deployment phases for service {ServiceName}", request.ServiceName);

            var canaryConfig = CanaryConfigurationExtensions.FromDictionary(request.Configuration);
            var canaryServers = GetCanaryServers(request.TargetServers, canaryConfig);
            var productionServers = GetProductionServers(request.TargetServers, canaryConfig);

            var phases = new List<DeploymentPhase>
            {
                CreatePreDeploymentPhase(request, canaryConfig),
                CreateCanaryDeploymentPhase(request, canaryServers, canaryConfig),
                CreateCanaryValidationPhase(request, canaryServers, canaryConfig),
                CreateTrafficRoutingPhase(request, canaryServers, canaryConfig),
                CreateCanaryMonitoringPhase(request, canaryServers, canaryConfig),
                CreateProductionDeploymentPhase(request, productionServers, canaryConfig),
                CreatePostDeploymentValidationPhase(request, productionServers, canaryConfig),
                CreateCanaryCleanupPhase(request, canaryServers, canaryConfig)
            };

            _logger.LogInformation("Created {PhaseCount} phases for canary deployment", phases.Count);
            return phases;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create canary deployment phases");
            throw;
        }
    }

    public async Task<bool> ValidateConfigurationAsync(
        Dictionary<string, object> configuration, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var requiredKeys = new[] { "CanaryConfiguration", "TrafficSplitting", "MonitoringConfiguration" };
            
            foreach (var key in requiredKeys)
            {
                if (!configuration.ContainsKey(key))
                {
                    _logger.LogError("Missing required configuration key: {Key}", key);
                    return false;
                }
            }

            // Validate canary configuration
            if (configuration["CanaryConfiguration"] is not Dictionary<string, object> canaryConfig)
            {
                _logger.LogError("Invalid CanaryConfiguration");
                return false;
            }

            // Validate traffic splitting configuration
            if (configuration["TrafficSplitting"] is not Dictionary<string, object> trafficConfig)
            {
                _logger.LogError("Invalid TrafficSplitting configuration");
                return false;
            }

            // Validate monitoring configuration
            if (configuration["MonitoringConfiguration"] is not Dictionary<string, object> monitoringConfig)
            {
                _logger.LogError("Invalid MonitoringConfiguration");
                return false;
            }

            // Validate canary percentage
            if (canaryConfig.TryGetValue("CanaryPercentage", out var percentageObj) && 
                percentageObj is double percentage)
            {
                if (percentage <= 0 || percentage > 100)
                {
                    _logger.LogError("Invalid CanaryPercentage: {Percentage}. Must be between 0 and 100", percentage);
                    return false;
                }
            }

            // Validate traffic splitting strategy
            if (trafficConfig.TryGetValue("Strategy", out var strategyObj))
            {
                if (!Enum.TryParse<TrafficSplittingStrategy>(strategyObj.ToString(), out _))
                {
                    _logger.LogError("Invalid TrafficSplittingStrategy: {Strategy}", strategyObj);
                    return false;
                }
            }

            _logger.LogInformation("Canary deployment configuration validated successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating canary deployment configuration");
            return false;
        }
    }

    public async Task<TimeSpan> EstimateExecutionTimeAsync(
        List<string> targetServers, 
        Dictionary<string, object> configuration,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canaryConfig = CanaryConfigurationExtensions.FromDictionary(configuration);
            var canaryServerCount = (int)Math.Ceiling(targetServers.Count * canaryConfig.CanaryPercentage / 100);
            var productionServerCount = targetServers.Count - canaryServerCount;
            
            // Base time estimations for canary deployment phases
            var preDeploymentTime = TimeSpan.FromMinutes(5);
            var canaryDeploymentTime = TimeSpan.FromMinutes(10 + (canaryServerCount * 3)); // 3 min per canary server
            var canaryValidationTime = TimeSpan.FromMinutes(5 + (canaryServerCount * 1)); // 1 min per canary server
            var trafficRoutingTime = TimeSpan.FromMinutes(5);
            var canaryMonitoringTime = canaryConfig.MonitoringDuration;
            var productionDeploymentTime = TimeSpan.FromMinutes(15 + (productionServerCount * 2)); // 2 min per prod server
            var postValidationTime = TimeSpan.FromMinutes(10);
            var cleanupTime = TimeSpan.FromMinutes(5);

            var totalTime = preDeploymentTime + canaryDeploymentTime + canaryValidationTime + 
                          trafficRoutingTime + canaryMonitoringTime + productionDeploymentTime + 
                          postValidationTime + cleanupTime;

            // Add 30% buffer for canary deployments due to monitoring requirements
            var bufferedTime = TimeSpan.FromMilliseconds(totalTime.TotalMilliseconds * 1.3);

            _logger.LogInformation("Estimated canary deployment time: {EstimatedTime} for {ServerCount} servers ({CanaryCount} canary)", 
                bufferedTime, targetServers.Count, canaryServerCount);

            return bufferedTime;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error estimating canary deployment execution time");
            return TimeSpan.FromHours(3); // Default fallback
        }
    }

    private DeploymentPhase CreatePreDeploymentPhase(DeploymentWorkflowRequest request, CanaryConfiguration config)
    {
        return new DeploymentPhase
        {
            Name = "Pre-Deployment Validation",
            Description = "Validate environment and prepare for canary deployment",
            TargetServers = request.TargetServers,
            Timeout = TimeSpan.FromMinutes(10),
            Steps = new List<DeploymentStep>
            {
                new DeploymentStep
                {
                    Name = "Validate Production Environment",
                    Type = StepType.Validation,
                    TargetServer = "monitoring-service",
                    Parameters = new Dictionary<string, object>
                    {
                        ["ValidationType"] = "ProductionHealth",
                        ["ServiceName"] = request.ServiceName,
                        ["Critical"] = true
                    }
                },
                new DeploymentStep
                {
                    Name = "Validate Monitoring Systems",
                    Type = StepType.Validation,
                    TargetServer = "monitoring-service",
                    Parameters = new Dictionary<string, object>
                    {
                        ["ValidationType"] = "MonitoringSystems",
                        ["RequiredMetrics"] = config.MonitoringConfiguration.RequiredMetrics,
                        ["Critical"] = true
                    }
                },
                new DeploymentStep
                {
                    Name = "Validate Traffic Routing Capability",
                    Type = StepType.Validation,
                    TargetServer = "load-balancer",
                    Parameters = new Dictionary<string, object>
                    {
                        ["ValidationType"] = "TrafficRouting",
                        ["SplittingStrategy"] = config.TrafficSplitting.Strategy.ToString(),
                        ["Critical"] = true
                    }
                },
                new DeploymentStep
                {
                    Name = "Create Deployment Baseline",
                    Type = StepType.Custom,
                    TargetServer = "metrics-collector",
                    Parameters = new Dictionary<string, object>
                    {
                        ["Action"] = "CreateBaseline",
                        ["ServiceName"] = request.ServiceName,
                        ["Duration"] = TimeSpan.FromMinutes(5),
                        ["Critical"] = false
                    }
                }
            }
        };
    }

    private DeploymentPhase CreateCanaryDeploymentPhase(DeploymentWorkflowRequest request, List<string> canaryServers, CanaryConfiguration config)
    {
        var steps = new List<DeploymentStep>();

        foreach (var server in canaryServers)
        {
            steps.AddRange(new[]
            {
                new DeploymentStep
                {
                    Name = $"Deploy Canary to {server}",
                    Type = StepType.Deploy,
                    TargetServer = server,
                    Parameters = new Dictionary<string, object>
                    {
                        ["PackageUrl"] = request.PackageUrl,
                        ["Version"] = request.Version,
                        ["ServiceName"] = request.ServiceName,
                        ["Environment"] = "Canary",
                        ["CanaryVersion"] = true,
                        ["Critical"] = true
                    }
                },
                new DeploymentStep
                {
                    Name = $"Start Canary Service on {server}",
                    Type = StepType.ServiceStart,
                    TargetServer = server,
                    Command = OrchestratorServiceCommand.Start,
                    Parameters = new Dictionary<string, object>
                    {
                        ["ServiceName"] = request.ServiceName,
                        ["CanaryMode"] = true,
                        ["Critical"] = true
                    }
                },
                new DeploymentStep
                {
                    Name = $"Wait for Canary Healthy on {server}",
                    Type = StepType.WaitForHealthy,
                    TargetServer = server,
                    Parameters = new Dictionary<string, object>
                    {
                        ["Timeout"] = config.HealthCheckTimeout,
                        ["ServiceName"] = request.ServiceName,
                        ["CanaryMode"] = true,
                        ["Critical"] = true
                    }
                }
            });
        }

        return new DeploymentPhase
        {
            Name = "Canary Deployment",
            Description = "Deploy new version to canary servers",
            TargetServers = canaryServers,
            Timeout = TimeSpan.FromMinutes(30),
            Steps = steps
        };
    }

    private DeploymentPhase CreateCanaryValidationPhase(DeploymentWorkflowRequest request, List<string> canaryServers, CanaryConfiguration config)
    {
        return new DeploymentPhase
        {
            Name = "Canary Validation",
            Description = "Validate canary deployment health and functionality",
            TargetServers = canaryServers,
            Timeout = TimeSpan.FromMinutes(15),
            Steps = new List<DeploymentStep>
            {
                new DeploymentStep
                {
                    Name = "Validate Canary Health",
                    Type = StepType.HealthCheck,
                    TargetServer = "health-check-service",
                    Parameters = new Dictionary<string, object>
                    {
                        ["TargetServers"] = canaryServers,
                        ["ServiceName"] = request.ServiceName,
                        ["Environment"] = "Canary",
                        ["Critical"] = true
                    }
                },
                new DeploymentStep
                {
                    Name = "Run Canary Smoke Tests",
                    Type = StepType.Validation,
                    TargetServer = "test-runner",
                    Parameters = new Dictionary<string, object>
                    {
                        ["TestType"] = "SmokeTests",
                        ["TargetServers"] = canaryServers,
                        ["ServiceName"] = request.ServiceName,
                        ["TestSuite"] = config.ValidationConfiguration.SmokeTestSuite,
                        ["Critical"] = true
                    }
                },
                new DeploymentStep
                {
                    Name = "Validate Canary Performance",
                    Type = StepType.Validation,
                    TargetServer = "performance-tester",
                    Parameters = new Dictionary<string, object>
                    {
                        ["ValidationType"] = "PerformanceBaseline",
                        ["TargetServers"] = canaryServers,
                        ["ServiceName"] = request.ServiceName,
                        ["AcceptableVariance"] = config.ValidationConfiguration.PerformanceThreshold,
                        ["Critical"] = true
                    }
                }
            }
        };
    }

    private DeploymentPhase CreateTrafficRoutingPhase(DeploymentWorkflowRequest request, List<string> canaryServers, CanaryConfiguration config)
    {
        return new DeploymentPhase
        {
            Name = "Traffic Routing Setup",
            Description = "Configure traffic routing to canary servers",
            TargetServers = new List<string> { "load-balancer" },
            Timeout = TimeSpan.FromMinutes(10),
            Steps = new List<DeploymentStep>
            {
                new DeploymentStep
                {
                    Name = "Configure Canary Traffic Routing",
                    Type = StepType.TrafficSwitch,
                    TargetServer = "load-balancer",
                    Parameters = new Dictionary<string, object>
                    {
                        ["Action"] = "ConfigureCanaryRouting",
                        ["CanaryServers"] = canaryServers,
                        ["CanaryPercentage"] = config.CanaryPercentage,
                        ["SplittingStrategy"] = config.TrafficSplitting.Strategy.ToString(),
                        ["RoutingRules"] = config.TrafficSplitting.RoutingRules,
                        ["ServiceName"] = request.ServiceName,
                        ["Critical"] = true
                    }
                },
                new DeploymentStep
                {
                    Name = "Validate Traffic Routing",
                    Type = StepType.Validation,
                    TargetServer = "traffic-validator",
                    Parameters = new Dictionary<string, object>
                    {
                        ["ValidationType"] = "CanaryTrafficSplit",
                        ["ExpectedPercentage"] = config.CanaryPercentage,
                        ["CanaryServers"] = canaryServers,
                        ["ServiceName"] = request.ServiceName,
                        ["ValidationDuration"] = TimeSpan.FromMinutes(2),
                        ["Critical"] = true
                    }
                },
                new DeploymentStep
                {
                    Name = "Initialize Canary Monitoring",
                    Type = StepType.Custom,
                    TargetServer = "monitoring-service",
                    Parameters = new Dictionary<string, object>
                    {
                        ["Action"] = "StartCanaryMonitoring",
                        ["CanaryServers"] = canaryServers,
                        ["ServiceName"] = request.ServiceName,
                        ["MonitoringConfiguration"] = config.MonitoringConfiguration.ToDictionary(),
                        ["Critical"] = false
                    }
                }
            }
        };
    }

    private DeploymentPhase CreateCanaryMonitoringPhase(DeploymentWorkflowRequest request, List<string> canaryServers, CanaryConfiguration config)
    {
        return new DeploymentPhase
        {
            Name = "Canary Monitoring",
            Description = "Monitor canary deployment performance and stability",
            TargetServers = canaryServers,
            Timeout = config.MonitoringDuration.Add(TimeSpan.FromMinutes(5)), // Add buffer
            Steps = new List<DeploymentStep>
            {
                new DeploymentStep
                {
                    Name = "Monitor Canary Performance",
                    Type = StepType.Validation,
                    TargetServer = "monitoring-service",
                    Parameters = new Dictionary<string, object>
                    {
                        ["ValidationType"] = "ContinuousMonitoring",
                        ["MonitoringDuration"] = config.MonitoringDuration,
                        ["CanaryServers"] = canaryServers,
                        ["ServiceName"] = request.ServiceName,
                        ["ErrorRateThreshold"] = config.RollbackTriggers.ErrorRateThreshold,
                        ["ResponseTimeThreshold"] = config.RollbackTriggers.ResponseTimeThreshold,
                        ["MemoryUsageThreshold"] = config.RollbackTriggers.MemoryUsageThreshold,
                        ["CpuUsageThreshold"] = config.RollbackTriggers.CpuUsageThreshold,
                        ["Critical"] = true
                    }
                },
                new DeploymentStep
                {
                    Name = "Validate Business Metrics",
                    Type = StepType.Validation,
                    TargetServer = "business-metrics-validator",
                    Parameters = new Dictionary<string, object>
                    {
                        ["ValidationType"] = "BusinessMetrics",
                        ["MonitoringDuration"] = config.MonitoringDuration,
                        ["ServiceName"] = request.ServiceName,
                        ["BusinessMetrics"] = config.MonitoringConfiguration.BusinessMetrics,
                        ["AcceptableVariance"] = config.ValidationConfiguration.BusinessMetricsThreshold,
                        ["Critical"] = false
                    }
                },
                new DeploymentStep
                {
                    Name = "Generate Canary Report",
                    Type = StepType.Custom,
                    TargetServer = "reporting-service",
                    Parameters = new Dictionary<string, object>
                    {
                        ["Action"] = "GenerateCanaryReport",
                        ["ServiceName"] = request.ServiceName,
                        ["CanaryServers"] = canaryServers,
                        ["MonitoringDuration"] = config.MonitoringDuration,
                        ["Critical"] = false
                    }
                }
            }
        };
    }

    private DeploymentPhase CreateProductionDeploymentPhase(DeploymentWorkflowRequest request, List<string> productionServers, CanaryConfiguration config)
    {
        var steps = new List<DeploymentStep>
        {
            new DeploymentStep
            {
                Name = "Validate Canary Success",
                Type = StepType.Validation,
                TargetServer = "canary-validator",
                Parameters = new Dictionary<string, object>
                {
                    ["ValidationType"] = "CanarySuccess",
                    ["ServiceName"] = request.ServiceName,
                    ["RequiredSuccessRate"] = config.ValidationConfiguration.SuccessRateThreshold,
                    ["Critical"] = true
                }
            }
        };

        // Deploy to production servers in batches if configured
        var batchSize = config.ProductionDeployment.BatchSize > 0 ? 
            config.ProductionDeployment.BatchSize : productionServers.Count;
        
        var batches = productionServers.Chunk(batchSize).ToList();
        
        for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
        {
            var batch = batches[batchIndex];
            
            foreach (var server in batch)
            {
                steps.AddRange(new[]
                {
                    new DeploymentStep
                    {
                        Name = $"Deploy Production to {server} (Batch {batchIndex + 1})",
                        Type = StepType.Deploy,
                        TargetServer = server,
                        Parameters = new Dictionary<string, object>
                        {
                            ["PackageUrl"] = request.PackageUrl,
                            ["Version"] = request.Version,
                            ["ServiceName"] = request.ServiceName,
                            ["Environment"] = "Production",
                            ["BatchIndex"] = batchIndex + 1,
                            ["Critical"] = true
                        }
                    },
                    new DeploymentStep
                    {
                        Name = $"Start Production Service on {server}",
                        Type = StepType.ServiceStart,
                        TargetServer = server,
                        Command = OrchestratorServiceCommand.Start,
                        Parameters = new Dictionary<string, object>
                        {
                            ["ServiceName"] = request.ServiceName,
                            ["Critical"] = true
                        }
                    },
                    new DeploymentStep
                    {
                        Name = $"Wait for Production Healthy on {server}",
                        Type = StepType.WaitForHealthy,
                        TargetServer = server,
                        Parameters = new Dictionary<string, object>
                        {
                            ["Timeout"] = config.HealthCheckTimeout,
                            ["ServiceName"] = request.ServiceName,
                            ["Critical"] = true
                        }
                    }
                });
            }

            // Add batch validation if not the last batch
            if (batchIndex < batches.Count - 1)
            {
                steps.Add(new DeploymentStep
                {
                    Name = $"Validate Batch {batchIndex + 1} Health",
                    Type = StepType.HealthCheck,
                    TargetServer = "health-check-service",
                    Parameters = new Dictionary<string, object>
                    {
                        ["TargetServers"] = batch.ToList(),
                        ["ServiceName"] = request.ServiceName,
                        ["Critical"] = true
                    }
                });

                if (config.ProductionDeployment.BatchDelay > TimeSpan.Zero)
                {
                    steps.Add(new DeploymentStep
                    {
                        Name = $"Wait Between Batches",
                        Type = StepType.Custom,
                        TargetServer = "scheduler",
                        Parameters = new Dictionary<string, object>
                        {
                            ["Action"] = "Wait",
                            ["Duration"] = config.ProductionDeployment.BatchDelay,
                            ["Critical"] = false
                        }
                    });
                }
            }
        }

        return new DeploymentPhase
        {
            Name = "Production Deployment",
            Description = "Deploy to remaining production servers",
            TargetServers = productionServers,
            Timeout = TimeSpan.FromMinutes(60),
            Steps = steps
        };
    }

    private DeploymentPhase CreatePostDeploymentValidationPhase(DeploymentWorkflowRequest request, List<string> productionServers, CanaryConfiguration config)
    {
        return new DeploymentPhase
        {
            Name = "Post-Deployment Validation",
            Description = "Validate complete deployment success",
            TargetServers = productionServers,
            Timeout = TimeSpan.FromMinutes(20),
            Steps = new List<DeploymentStep>
            {
                new DeploymentStep
                {
                    Name = "Validate All Servers Health",
                    Type = StepType.HealthCheck,
                    TargetServer = "health-check-service",
                    Parameters = new Dictionary<string, object>
                    {
                        ["TargetServers"] = request.TargetServers,
                        ["ServiceName"] = request.ServiceName,
                        ["Critical"] = true
                    }
                },
                new DeploymentStep
                {
                    Name = "Run Full Integration Tests",
                    Type = StepType.Validation,
                    TargetServer = "test-runner",
                    Parameters = new Dictionary<string, object>
                    {
                        ["TestType"] = "IntegrationTests",
                        ["ServiceName"] = request.ServiceName,
                        ["TestSuite"] = config.ValidationConfiguration.IntegrationTestSuite,
                        ["Critical"] = true
                    }
                },
                new DeploymentStep
                {
                    Name = "Update Traffic Routing to Full Production",
                    Type = StepType.TrafficSwitch,
                    TargetServer = "load-balancer",
                    Parameters = new Dictionary<string, object>
                    {
                        ["Action"] = "RemoveCanaryRouting",
                        ["ServiceName"] = request.ServiceName,
                        ["Critical"] = true
                    }
                },
                new DeploymentStep
                {
                    Name = "Monitor Post-Deployment Performance",
                    Type = StepType.Validation,
                    TargetServer = "monitoring-service",
                    Parameters = new Dictionary<string, object>
                    {
                        ["ValidationType"] = "PostDeploymentMonitoring",
                        ["MonitoringDuration"] = TimeSpan.FromMinutes(10),
                        ["ServiceName"] = request.ServiceName,
                        ["Critical"] = false
                    }
                }
            }
        };
    }

    private DeploymentPhase CreateCanaryCleanupPhase(DeploymentWorkflowRequest request, List<string> canaryServers, CanaryConfiguration config)
    {
        return new DeploymentPhase
        {
            Name = "Canary Cleanup",
            Description = "Clean up canary deployment artifacts",
            TargetServers = canaryServers,
            Timeout = TimeSpan.FromMinutes(10),
            RollbackOnFailure = false, // Don't rollback during cleanup
            Steps = new List<DeploymentStep>
            {
                new DeploymentStep
                {
                    Name = "Generate Final Deployment Report",
                    Type = StepType.Custom,
                    TargetServer = "reporting-service",
                    Parameters = new Dictionary<string, object>
                    {
                        ["Action"] = "GenerateFinalReport",
                        ["ServiceName"] = request.ServiceName,
                        ["DeploymentType"] = "Canary",
                        ["CanaryServers"] = canaryServers,
                        ["Critical"] = false
                    }
                },
                new DeploymentStep
                {
                    Name = "Archive Canary Metrics",
                    Type = StepType.Custom,
                    TargetServer = "metrics-archiver",
                    Parameters = new Dictionary<string, object>
                    {
                        ["Action"] = "ArchiveMetrics",
                        ["ServiceName"] = request.ServiceName,
                        ["DeploymentId"] = request.Metadata.GetValueOrDefault("DeploymentId", ""),
                        ["Critical"] = false
                    }
                },
                new DeploymentStep
                {
                    Name = "Update Deployment Registry",
                    Type = StepType.Custom,
                    TargetServer = "deployment-registry",
                    Parameters = new Dictionary<string, object>
                    {
                        ["Action"] = "UpdateRegistry",
                        ["ServiceName"] = request.ServiceName,
                        ["Version"] = request.Version,
                        ["DeploymentStrategy"] = "Canary",
                        ["Status"] = "Completed",
                        ["Critical"] = false
                    }
                }
            }
        };
    }

    private List<string> GetCanaryServers(List<string> targetServers, CanaryConfiguration config)
    {
        if (config.DeploymentConfiguration.ExplicitCanaryServers?.Any() == true)
        {
            return config.DeploymentConfiguration.ExplicitCanaryServers;
        }

        var canaryCount = (int)Math.Ceiling(targetServers.Count * config.CanaryPercentage / 100);
        return targetServers.Take(canaryCount).ToList();
    }

    private List<string> GetProductionServers(List<string> targetServers, CanaryConfiguration config)
    {
        var canaryServers = GetCanaryServers(targetServers, config);
        return targetServers.Except(canaryServers).ToList();
    }
}