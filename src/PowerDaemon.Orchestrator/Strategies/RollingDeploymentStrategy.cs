using Microsoft.Extensions.Logging;
using PowerDaemon.Orchestrator.Models;
using PowerDaemon.Orchestrator.Services;
using PowerDaemon.Messaging.Messages;
using OrchestratorServiceCommand = PowerDaemon.Orchestrator.Models.ServiceCommand;

namespace PowerDaemon.Orchestrator.Strategies;

public class RollingDeploymentStrategy : IDeploymentStrategy
{
    private readonly ILogger<RollingDeploymentStrategy> _logger;

    public DeploymentStrategy StrategyType => DeploymentStrategy.Rolling;

    public RollingDeploymentStrategy(ILogger<RollingDeploymentStrategy> logger)
    {
        _logger = logger;
    }

    public async Task<List<DeploymentPhase>> CreatePhasesAsync(
        DeploymentWorkflowRequest request, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Creating rolling deployment phases for service {ServiceName}", request.ServiceName);

            var rollingConfig = RollingConfigurationExtensions.FromDictionary(request.Configuration);
            var serverWaves = CreateServerWaves(request.TargetServers, rollingConfig);

            var phases = new List<DeploymentPhase>
            {
                CreatePreDeploymentPhase(request, rollingConfig),
                CreatePreRollingValidationPhase(request, rollingConfig)
            };

            // Create a deployment phase for each wave
            for (int waveIndex = 0; waveIndex < serverWaves.Count; waveIndex++)
            {
                var wave = serverWaves[waveIndex];
                phases.Add(CreateWaveDeploymentPhase(request, wave, waveIndex + 1, rollingConfig));
                phases.Add(CreateWaveValidationPhase(request, wave, waveIndex + 1, rollingConfig));
                
                // Add wave monitoring phase if not the last wave
                if (waveIndex < serverWaves.Count - 1)
                {
                    phases.Add(CreateWaveMonitoringPhase(request, wave, waveIndex + 1, rollingConfig));
                }
            }

            phases.Add(CreatePostDeploymentValidationPhase(request, rollingConfig));
            phases.Add(CreateRollingCleanupPhase(request, rollingConfig));

            _logger.LogInformation("Created {PhaseCount} phases for rolling deployment with {WaveCount} waves", 
                phases.Count, serverWaves.Count);
            return phases;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create rolling deployment phases");
            throw;
        }
    }

    public async Task<bool> ValidateConfigurationAsync(
        Dictionary<string, object> configuration, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var requiredKeys = new[] { "RollingConfiguration", "WaveConfiguration", "HealthCheckConfiguration" };
            
            foreach (var key in requiredKeys)
            {
                if (!configuration.ContainsKey(key))
                {
                    _logger.LogError("Missing required configuration key: {Key}", key);
                    return false;
                }
            }

            // Validate rolling configuration
            if (configuration["RollingConfiguration"] is not Dictionary<string, object> rollingConfig)
            {
                _logger.LogError("Invalid RollingConfiguration");
                return false;
            }

            // Validate wave configuration
            if (configuration["WaveConfiguration"] is not Dictionary<string, object> waveConfig)
            {
                _logger.LogError("Invalid WaveConfiguration");
                return false;
            }

            // Validate health check configuration
            if (configuration["HealthCheckConfiguration"] is not Dictionary<string, object> healthConfig)
            {
                _logger.LogError("Invalid HealthCheckConfiguration");
                return false;
            }

            // Validate wave strategy
            if (waveConfig.TryGetValue("Strategy", out var strategyObj))
            {
                if (!Enum.TryParse<WaveStrategy>(strategyObj.ToString(), out _))
                {
                    _logger.LogError("Invalid WaveStrategy: {Strategy}", strategyObj);
                    return false;
                }
            }

            // Validate wave size or percentage
            if (waveConfig.TryGetValue("WaveSize", out var waveSizeObj) && waveSizeObj is int waveSize)
            {
                if (waveSize <= 0)
                {
                    _logger.LogError("Invalid WaveSize: {WaveSize}. Must be greater than 0", waveSize);
                    return false;
                }
            }

            if (waveConfig.TryGetValue("WavePercentage", out var wavePercentageObj) && wavePercentageObj is double wavePercentage)
            {
                if (wavePercentage <= 0 || wavePercentage > 100)
                {
                    _logger.LogError("Invalid WavePercentage: {WavePercentage}. Must be between 0 and 100", wavePercentage);
                    return false;
                }
            }

            _logger.LogInformation("Rolling deployment configuration validated successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating rolling deployment configuration");
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
            var rollingConfig = RollingConfigurationExtensions.FromDictionary(configuration);
            var serverWaves = CreateServerWaves(targetServers, rollingConfig);
            
            // Base time estimations for rolling deployment phases
            var preDeploymentTime = TimeSpan.FromMinutes(5);
            var preValidationTime = TimeSpan.FromMinutes(5);
            
            // Calculate time per wave
            var waveDeploymentTime = TimeSpan.Zero;
            var waveValidationTime = TimeSpan.Zero;
            var waveMonitoringTime = TimeSpan.Zero;

            foreach (var wave in serverWaves)
            {
                var serversInWave = wave.Count;
                var parallelDeployment = rollingConfig.WaveConfiguration.ParallelDeploymentWithinWave;
                
                if (parallelDeployment)
                {
                    // Parallel deployment: time based on slowest server + buffer
                    waveDeploymentTime = waveDeploymentTime.Add(TimeSpan.FromMinutes(10 + Math.Max(2, serversInWave / 2)));
                }
                else
                {
                    // Sequential deployment: time per server
                    waveDeploymentTime = waveDeploymentTime.Add(TimeSpan.FromMinutes(5 + (serversInWave * 3)));
                }
                
                waveValidationTime = waveValidationTime.Add(TimeSpan.FromMinutes(3 + serversInWave));
                waveMonitoringTime = waveMonitoringTime.Add(rollingConfig.WaveConfiguration.WaveInterval);
            }

            var postValidationTime = TimeSpan.FromMinutes(10);
            var cleanupTime = TimeSpan.FromMinutes(5);

            var totalTime = preDeploymentTime + preValidationTime + waveDeploymentTime + 
                          waveValidationTime + waveMonitoringTime + postValidationTime + cleanupTime;

            // Add 25% buffer for rolling deployments
            var bufferedTime = TimeSpan.FromMilliseconds(totalTime.TotalMilliseconds * 1.25);

            _logger.LogInformation("Estimated rolling deployment time: {EstimatedTime} for {ServerCount} servers in {WaveCount} waves", 
                bufferedTime, targetServers.Count, serverWaves.Count);

            return bufferedTime;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error estimating rolling deployment execution time");
            return TimeSpan.FromHours(2); // Default fallback
        }
    }

    private DeploymentPhase CreatePreDeploymentPhase(DeploymentWorkflowRequest request, RollingConfiguration config)
    {
        return new DeploymentPhase
        {
            Name = "Pre-Deployment Validation",
            Description = "Validate environment and prepare for rolling deployment",
            TargetServers = request.TargetServers,
            Timeout = TimeSpan.FromMinutes(10),
            Steps = new List<DeploymentStep>
            {
                new DeploymentStep
                {
                    Name = "Validate Current Environment",
                    Type = StepType.Validation,
                    TargetServer = "environment-validator",
                    Parameters = new Dictionary<string, object>
                    {
                        ["ValidationType"] = "EnvironmentHealth",
                        ["ServiceName"] = request.ServiceName,
                        ["TargetServers"] = request.TargetServers,
                        ["Critical"] = true
                    }
                },
                new DeploymentStep
                {
                    Name = "Validate Load Balancer Configuration",
                    Type = StepType.Validation,
                    TargetServer = "load-balancer",
                    Parameters = new Dictionary<string, object>
                    {
                        ["ValidationType"] = "LoadBalancerHealth",
                        ["ServiceName"] = request.ServiceName,
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
                        ["TargetServers"] = request.TargetServers,
                        ["BaselineDuration"] = TimeSpan.FromMinutes(5),
                        ["Critical"] = false
                    }
                },
                new DeploymentStep
                {
                    Name = "Backup Current Configuration",
                    Type = StepType.Custom,
                    TargetServer = "backup-service",
                    Parameters = new Dictionary<string, object>
                    {
                        ["BackupType"] = "Configuration",
                        ["ServiceName"] = request.ServiceName,
                        ["TargetServers"] = request.TargetServers,
                        ["Critical"] = false
                    }
                }
            }
        };
    }

    private DeploymentPhase CreatePreRollingValidationPhase(DeploymentWorkflowRequest request, RollingConfiguration config)
    {
        return new DeploymentPhase
        {
            Name = "Pre-Rolling Validation",
            Description = "Validate rolling deployment prerequisites",
            TargetServers = request.TargetServers,
            Timeout = TimeSpan.FromMinutes(10),
            Steps = new List<DeploymentStep>
            {
                new DeploymentStep
                {
                    Name = "Validate Service Dependencies",
                    Type = StepType.Validation,
                    TargetServer = "dependency-validator",
                    Parameters = new Dictionary<string, object>
                    {
                        ["ValidationType"] = "ServiceDependencies",
                        ["ServiceName"] = request.ServiceName,
                        ["Dependencies"] = config.DeploymentConfiguration.ServiceDependencies,
                        ["Critical"] = true
                    }
                },
                new DeploymentStep
                {
                    Name = "Validate Minimum Available Instances",
                    Type = StepType.Validation,
                    TargetServer = "capacity-validator",
                    Parameters = new Dictionary<string, object>
                    {
                        ["ValidationType"] = "MinimumCapacity",
                        ["ServiceName"] = request.ServiceName,
                        ["TotalServers"] = request.TargetServers.Count,
                        ["MinimumAvailableInstances"] = config.DeploymentConfiguration.MinimumAvailableInstances,
                        ["Critical"] = true
                    }
                },
                new DeploymentStep
                {
                    Name = "Initialize Rolling Deployment Monitoring",
                    Type = StepType.Custom,
                    TargetServer = "monitoring-service",
                    Parameters = new Dictionary<string, object>
                    {
                        ["Action"] = "InitializeRollingMonitoring",
                        ["ServiceName"] = request.ServiceName,
                        ["MonitoringConfiguration"] = config.MonitoringConfiguration.ToDictionary(),
                        ["Critical"] = false
                    }
                }
            }
        };
    }

    private DeploymentPhase CreateWaveDeploymentPhase(DeploymentWorkflowRequest request, List<string> waveServers, int waveNumber, RollingConfiguration config)
    {
        var steps = new List<DeploymentStep>
        {
            new DeploymentStep
            {
                Name = $"Prepare Wave {waveNumber} Servers",
                Type = StepType.Custom,
                TargetServer = "wave-coordinator",
                Parameters = new Dictionary<string, object>
                {
                    ["Action"] = "PrepareWave",
                    ["WaveNumber"] = waveNumber,
                    ["WaveServers"] = waveServers,
                    ["ServiceName"] = request.ServiceName,
                    ["Critical"] = false
                }
            }
        };

        if (config.WaveConfiguration.ParallelDeploymentWithinWave)
        {
            // Deploy to all servers in wave in parallel
            steps.Add(new DeploymentStep
            {
                Name = $"Deploy Wave {waveNumber} (Parallel)",
                Type = StepType.Deploy,
                TargetServer = "parallel-deployer",
                Parameters = new Dictionary<string, object>
                {
                    ["PackageUrl"] = request.PackageUrl,
                    ["Version"] = request.Version,
                    ["ServiceName"] = request.ServiceName,
                    ["TargetServers"] = waveServers,
                    ["WaveNumber"] = waveNumber,
                    ["ParallelDeployment"] = true,
                    ["MaxParallelism"] = config.WaveConfiguration.MaxParallelism,
                    ["Critical"] = true
                }
            });

            steps.Add(new DeploymentStep
            {
                Name = $"Start Services Wave {waveNumber} (Parallel)",
                Type = StepType.ServiceStart,
                TargetServer = "parallel-service-manager",
                Command = OrchestratorServiceCommand.Start,
                Parameters = new Dictionary<string, object>
                {
                    ["ServiceName"] = request.ServiceName,
                    ["TargetServers"] = waveServers,
                    ["WaveNumber"] = waveNumber,
                    ["Critical"] = true
                }
            });
        }
        else
        {
            // Deploy to servers sequentially within the wave
            foreach (var server in waveServers)
            {
                steps.AddRange(new[]
                {
                    new DeploymentStep
                    {
                        Name = $"Remove {server} from Load Balancer",
                        Type = StepType.TrafficSwitch,
                        TargetServer = "load-balancer",
                        Parameters = new Dictionary<string, object>
                        {
                            ["Action"] = "RemoveServer",
                            ["TargetServer"] = server,
                            ["ServiceName"] = request.ServiceName,
                            ["WaveNumber"] = waveNumber,
                            ["Critical"] = true
                        }
                    },
                    new DeploymentStep
                    {
                        Name = $"Deploy to {server} (Wave {waveNumber})",
                        Type = StepType.Deploy,
                        TargetServer = server,
                        Parameters = new Dictionary<string, object>
                        {
                            ["PackageUrl"] = request.PackageUrl,
                            ["Version"] = request.Version,
                            ["ServiceName"] = request.ServiceName,
                            ["WaveNumber"] = waveNumber,
                            ["Critical"] = true
                        }
                    },
                    new DeploymentStep
                    {
                        Name = $"Start Service on {server}",
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
                        Name = $"Wait for {server} Healthy",
                        Type = StepType.WaitForHealthy,
                        TargetServer = server,
                        Parameters = new Dictionary<string, object>
                        {
                            ["Timeout"] = config.HealthCheckConfiguration.HealthCheckTimeout,
                            ["ServiceName"] = request.ServiceName,
                            ["Critical"] = true
                        }
                    },
                    new DeploymentStep
                    {
                        Name = $"Add {server} back to Load Balancer",
                        Type = StepType.TrafficSwitch,
                        TargetServer = "load-balancer",
                        Parameters = new Dictionary<string, object>
                        {
                            ["Action"] = "AddServer",
                            ["TargetServer"] = server,
                            ["ServiceName"] = request.ServiceName,
                            ["WaveNumber"] = waveNumber,
                            ["Critical"] = true
                        }
                    }
                });

                // Add delay between servers if configured
                if (config.WaveConfiguration.DelayBetweenServers > TimeSpan.Zero)
                {
                    steps.Add(new DeploymentStep
                    {
                        Name = $"Wait Between Server Deployments",
                        Type = StepType.Custom,
                        TargetServer = "scheduler",
                        Parameters = new Dictionary<string, object>
                        {
                            ["Action"] = "Wait",
                            ["Duration"] = config.WaveConfiguration.DelayBetweenServers,
                            ["Critical"] = false
                        }
                    });
                }
            }
        }

        return new DeploymentPhase
        {
            Name = $"Wave {waveNumber} Deployment",
            Description = $"Deploy to wave {waveNumber} servers ({waveServers.Count} servers)",
            TargetServers = waveServers,
            Timeout = TimeSpan.FromMinutes(30 + (waveServers.Count * 5)),
            Steps = steps
        };
    }

    private DeploymentPhase CreateWaveValidationPhase(DeploymentWorkflowRequest request, List<string> waveServers, int waveNumber, RollingConfiguration config)
    {
        return new DeploymentPhase
        {
            Name = $"Wave {waveNumber} Validation",
            Description = $"Validate wave {waveNumber} deployment success",
            TargetServers = waveServers,
            Timeout = TimeSpan.FromMinutes(15),
            Steps = new List<DeploymentStep>
            {
                new DeploymentStep
                {
                    Name = $"Validate Wave {waveNumber} Health",
                    Type = StepType.HealthCheck,
                    TargetServer = "health-check-service",
                    Parameters = new Dictionary<string, object>
                    {
                        ["TargetServers"] = waveServers,
                        ["ServiceName"] = request.ServiceName,
                        ["WaveNumber"] = waveNumber,
                        ["Critical"] = true
                    }
                },
                new DeploymentStep
                {
                    Name = $"Run Wave {waveNumber} Smoke Tests",
                    Type = StepType.Validation,
                    TargetServer = "test-runner",
                    Parameters = new Dictionary<string, object>
                    {
                        ["TestType"] = "SmokeTests",
                        ["TargetServers"] = waveServers,
                        ["ServiceName"] = request.ServiceName,
                        ["WaveNumber"] = waveNumber,
                        ["TestSuite"] = config.ValidationConfiguration.SmokeTestSuite,
                        ["Critical"] = true
                    }
                },
                new DeploymentStep
                {
                    Name = $"Validate Wave {waveNumber} Performance",
                    Type = StepType.Validation,
                    TargetServer = "performance-validator",
                    Parameters = new Dictionary<string, object>
                    {
                        ["ValidationType"] = "PerformanceValidation",
                        ["TargetServers"] = waveServers,
                        ["ServiceName"] = request.ServiceName,
                        ["WaveNumber"] = waveNumber,
                        ["PerformanceThreshold"] = config.ValidationConfiguration.PerformanceThreshold,
                        ["Critical"] = true
                    }
                },
                new DeploymentStep
                {
                    Name = $"Validate Service Capacity",
                    Type = StepType.Validation,
                    TargetServer = "capacity-validator",
                    Parameters = new Dictionary<string, object>
                    {
                        ["ValidationType"] = "ServiceCapacity",
                        ["ServiceName"] = request.ServiceName,
                        ["WaveNumber"] = waveNumber,
                        ["ExpectedCapacityIncrease"] = waveServers.Count,
                        ["Critical"] = false
                    }
                }
            }
        };
    }

    private DeploymentPhase CreateWaveMonitoringPhase(DeploymentWorkflowRequest request, List<string> waveServers, int waveNumber, RollingConfiguration config)
    {
        return new DeploymentPhase
        {
            Name = $"Wave {waveNumber} Monitoring",
            Description = $"Monitor wave {waveNumber} stability before next wave",
            TargetServers = waveServers,
            Timeout = config.WaveConfiguration.WaveInterval.Add(TimeSpan.FromMinutes(5)),
            Steps = new List<DeploymentStep>
            {
                new DeploymentStep
                {
                    Name = $"Monitor Wave {waveNumber} Stability",
                    Type = StepType.Validation,
                    TargetServer = "monitoring-service",
                    Parameters = new Dictionary<string, object>
                    {
                        ["ValidationType"] = "StabilityMonitoring",
                        ["MonitoringDuration"] = config.WaveConfiguration.WaveInterval,
                        ["TargetServers"] = waveServers,
                        ["ServiceName"] = request.ServiceName,
                        ["WaveNumber"] = waveNumber,
                        ["ErrorRateThreshold"] = config.RollbackTriggers.ErrorRateThreshold,
                        ["ResponseTimeThreshold"] = config.RollbackTriggers.ResponseTimeThreshold,
                        ["MemoryUsageThreshold"] = config.RollbackTriggers.MemoryUsageThreshold,
                        ["Critical"] = true
                    }
                },
                new DeploymentStep
                {
                    Name = $"Validate Overall System Health",
                    Type = StepType.HealthCheck,
                    TargetServer = "system-health-monitor",
                    Parameters = new Dictionary<string, object>
                    {
                        ["ValidationType"] = "SystemHealth",
                        ["ServiceName"] = request.ServiceName,
                        ["WaveNumber"] = waveNumber,
                        ["IncludeUpstreamServices"] = true,
                        ["IncludeDownstreamServices"] = true,
                        ["Critical"] = true
                    }
                },
                new DeploymentStep
                {
                    Name = $"Generate Wave {waveNumber} Report",
                    Type = StepType.Custom,
                    TargetServer = "reporting-service",
                    Parameters = new Dictionary<string, object>
                    {
                        ["Action"] = "GenerateWaveReport",
                        ["ServiceName"] = request.ServiceName,
                        ["WaveNumber"] = waveNumber,
                        ["WaveServers"] = waveServers,
                        ["MonitoringDuration"] = config.WaveConfiguration.WaveInterval,
                        ["Critical"] = false
                    }
                }
            }
        };
    }

    private DeploymentPhase CreatePostDeploymentValidationPhase(DeploymentWorkflowRequest request, RollingConfiguration config)
    {
        return new DeploymentPhase
        {
            Name = "Post-Deployment Validation",
            Description = "Validate complete rolling deployment success",
            TargetServers = request.TargetServers,
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
                    Name = "Validate Load Balancer Configuration",
                    Type = StepType.Validation,
                    TargetServer = "load-balancer",
                    Parameters = new Dictionary<string, object>
                    {
                        ["ValidationType"] = "LoadBalancerConfiguration",
                        ["ServiceName"] = request.ServiceName,
                        ["ExpectedServerCount"] = request.TargetServers.Count,
                        ["Critical"] = true
                    }
                },
                new DeploymentStep
                {
                    Name = "Validate Service Performance",
                    Type = StepType.Validation,
                    TargetServer = "performance-validator",
                    Parameters = new Dictionary<string, object>
                    {
                        ["ValidationType"] = "ServicePerformance",
                        ["ServiceName"] = request.ServiceName,
                        ["PerformanceTestDuration"] = TimeSpan.FromMinutes(5),
                        ["LoadTestConfiguration"] = config.ValidationConfiguration.LoadTestConfiguration,
                        ["Critical"] = false
                    }
                },
                new DeploymentStep
                {
                    Name = "Monitor Post-Deployment Stability",
                    Type = StepType.Validation,
                    TargetServer = "monitoring-service",
                    Parameters = new Dictionary<string, object>
                    {
                        ["ValidationType"] = "PostDeploymentStability",
                        ["MonitoringDuration"] = TimeSpan.FromMinutes(10),
                        ["ServiceName"] = request.ServiceName,
                        ["Critical"] = false
                    }
                }
            }
        };
    }

    private DeploymentPhase CreateRollingCleanupPhase(DeploymentWorkflowRequest request, RollingConfiguration config)
    {
        return new DeploymentPhase
        {
            Name = "Rolling Deployment Cleanup",
            Description = "Clean up rolling deployment artifacts and finalize",
            TargetServers = request.TargetServers,
            Timeout = TimeSpan.FromMinutes(10),
            RollbackOnFailure = false, // Don't rollback during cleanup
            Steps = new List<DeploymentStep>
            {
                new DeploymentStep
                {
                    Name = "Generate Final Rolling Deployment Report",
                    Type = StepType.Custom,
                    TargetServer = "reporting-service",
                    Parameters = new Dictionary<string, object>
                    {
                        ["Action"] = "GenerateFinalReport",
                        ["ServiceName"] = request.ServiceName,
                        ["DeploymentType"] = "Rolling",
                        ["TargetServers"] = request.TargetServers,
                        ["TotalWaves"] = CreateServerWaves(request.TargetServers, config).Count,
                        ["Critical"] = false
                    }
                },
                new DeploymentStep
                {
                    Name = "Archive Rolling Deployment Metrics",
                    Type = StepType.Custom,
                    TargetServer = "metrics-archiver",
                    Parameters = new Dictionary<string, object>
                    {
                        ["Action"] = "ArchiveMetrics",
                        ["ServiceName"] = request.ServiceName,
                        ["DeploymentId"] = request.Metadata.GetValueOrDefault("DeploymentId", ""),
                        ["DeploymentType"] = "Rolling",
                        ["Critical"] = false
                    }
                },
                new DeploymentStep
                {
                    Name = "Clean Up Temporary Artifacts",
                    Type = StepType.Cleanup,
                    TargetServer = "cleanup-service",
                    Parameters = new Dictionary<string, object>
                    {
                        ["CleanupType"] = "TemporaryArtifacts",
                        ["ServiceName"] = request.ServiceName,
                        ["TargetServers"] = request.TargetServers,
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
                        ["DeploymentStrategy"] = "Rolling",
                        ["Status"] = "Completed",
                        ["Critical"] = false
                    }
                }
            }
        };
    }

    private List<List<string>> CreateServerWaves(List<string> targetServers, RollingConfiguration config)
    {
        var waves = new List<List<string>>();
        var remainingServers = new List<string>(targetServers);

        switch (config.WaveConfiguration.Strategy)
        {
            case WaveStrategy.FixedSize:
                var waveSize = config.WaveConfiguration.WaveSize > 0 ? 
                    config.WaveConfiguration.WaveSize : 
                    Math.Max(1, targetServers.Count / 4); // Default to 25% per wave

                while (remainingServers.Count > 0)
                {
                    var currentWaveSize = Math.Min(waveSize, remainingServers.Count);
                    waves.Add(remainingServers.Take(currentWaveSize).ToList());
                    remainingServers.RemoveRange(0, currentWaveSize);
                }
                break;

            case WaveStrategy.Percentage:
                var wavePercentage = config.WaveConfiguration.WavePercentage > 0 ? 
                    config.WaveConfiguration.WavePercentage : 25.0; // Default to 25%

                while (remainingServers.Count > 0)
                {
                    var currentWaveSize = Math.Max(1, (int)Math.Ceiling(remainingServers.Count * wavePercentage / 100));
                    currentWaveSize = Math.Min(currentWaveSize, remainingServers.Count);
                    waves.Add(remainingServers.Take(currentWaveSize).ToList());
                    remainingServers.RemoveRange(0, currentWaveSize);
                }
                break;

            case WaveStrategy.Geographic:
                waves.AddRange(CreateGeographicWaves(targetServers, config));
                break;

            case WaveStrategy.Custom:
                waves.AddRange(CreateCustomWaves(targetServers, config));
                break;

            default:
                // Fallback to fixed size strategy
                var fallbackConfig = new RollingConfiguration
                {
                    DeploymentConfiguration = config.DeploymentConfiguration,
                    WaveConfiguration = new WaveConfiguration
                    {
                        Strategy = WaveStrategy.FixedSize,
                        WaveSize = config.WaveConfiguration.WaveSize,
                        WavePercentage = config.WaveConfiguration.WavePercentage,
                        WaveInterval = config.WaveConfiguration.WaveInterval,
                        ParallelDeploymentWithinWave = config.WaveConfiguration.ParallelDeploymentWithinWave,
                        MaxParallelism = config.WaveConfiguration.MaxParallelism,
                        DelayBetweenServers = config.WaveConfiguration.DelayBetweenServers,
                        AdaptiveWaveSizing = config.WaveConfiguration.AdaptiveWaveSizing,
                        GeographicConfiguration = config.WaveConfiguration.GeographicConfiguration,
                        CustomWaves = config.WaveConfiguration.CustomWaves,
                        RollbackStrategy = config.WaveConfiguration.RollbackStrategy
                    },
                    HealthCheckConfiguration = config.HealthCheckConfiguration,
                    MonitoringConfiguration = config.MonitoringConfiguration,
                    ValidationConfiguration = config.ValidationConfiguration,
                    RollbackTriggers = config.RollbackTriggers,
                    LoadBalancerConfiguration = config.LoadBalancerConfiguration
                };
                waves.AddRange(CreateServerWaves(targetServers, fallbackConfig));
                break;
        }

        return waves;
    }

    private List<List<string>> CreateGeographicWaves(List<string> targetServers, RollingConfiguration config)
    {
        var waves = new List<List<string>>();
        
        // Group servers by geographic location if configuration is available
        if (config.WaveConfiguration.GeographicConfiguration?.Any() == true)
        {
            foreach (var geoConfig in config.WaveConfiguration.GeographicConfiguration)
            {
                var geoServers = targetServers.Where(server => 
                    geoConfig.ServerPatterns.Any(pattern => server.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                ).ToList();
                
                if (geoServers.Any())
                {
                    waves.Add(geoServers);
                }
            }
        }
        
        // Add any remaining servers that don't match geographic patterns
        var remainingServers = targetServers.Except(waves.SelectMany(w => w)).ToList();
        if (remainingServers.Any())
        {
            waves.Add(remainingServers);
        }

        return waves;
    }

    private List<List<string>> CreateCustomWaves(List<string> targetServers, RollingConfiguration config)
    {
        var waves = new List<List<string>>();
        
        if (config.WaveConfiguration.CustomWaves?.Any() == true)
        {
            foreach (var customWave in config.WaveConfiguration.CustomWaves)
            {
                var waveServers = targetServers.Where(server => 
                    customWave.ServerList.Contains(server, StringComparer.OrdinalIgnoreCase)
                ).ToList();
                
                if (waveServers.Any())
                {
                    waves.Add(waveServers);
                }
            }
        }
        
        // Add any servers not included in custom waves
        var remainingServers = targetServers.Except(waves.SelectMany(w => w)).ToList();
        if (remainingServers.Any())
        {
            // Split remaining servers into reasonable waves
            var defaultWaveSize = Math.Max(1, remainingServers.Count / 3);
            while (remainingServers.Count > 0)
            {
                var currentWaveSize = Math.Min(defaultWaveSize, remainingServers.Count);
                waves.Add(remainingServers.Take(currentWaveSize).ToList());
                remainingServers.RemoveRange(0, currentWaveSize);
            }
        }

        return waves;
    }
}