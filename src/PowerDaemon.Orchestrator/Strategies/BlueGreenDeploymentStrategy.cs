using Microsoft.Extensions.Logging;
using PowerDaemon.Orchestrator.Models;
using PowerDaemon.Orchestrator.Services;
using PowerDaemon.Messaging.Messages;
using OrchestratorServiceCommand = PowerDaemon.Orchestrator.Models.ServiceCommand;

namespace PowerDaemon.Orchestrator.Strategies;

public class BlueGreenDeploymentStrategy : IDeploymentStrategy
{
    private readonly ILogger<BlueGreenDeploymentStrategy> _logger;

    public DeploymentStrategy StrategyType => DeploymentStrategy.BlueGreen;

    public BlueGreenDeploymentStrategy(ILogger<BlueGreenDeploymentStrategy> logger)
    {
        _logger = logger;
    }

    public async Task<List<DeploymentPhase>> CreatePhasesAsync(
        DeploymentWorkflowRequest request, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Creating blue-green deployment phases for service {ServiceName}", request.ServiceName);

            var blueServers = GetBlueServers(request.TargetServers, request.Configuration);
            var greenServers = GetGreenServers(request.TargetServers, request.Configuration);

            var phases = new List<DeploymentPhase>
            {
                CreatePreDeploymentPhase(request),
                CreateGreenEnvironmentPreparationPhase(request, greenServers),
                CreateGreenDeploymentPhase(request, greenServers),
                CreateGreenValidationPhase(request, greenServers),
                CreateTrafficSwitchPhase(request, blueServers, greenServers),
                CreateBlueEnvironmentValidationPhase(request, blueServers),
                CreatePostDeploymentCleanupPhase(request, blueServers)
            };

            _logger.LogInformation("Created {PhaseCount} phases for blue-green deployment", phases.Count);
            return phases;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create blue-green deployment phases");
            throw;
        }
    }

    public async Task<bool> ValidateConfigurationAsync(
        Dictionary<string, object> configuration, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var requiredKeys = new[] { "BlueEnvironment", "GreenEnvironment", "LoadBalancerConfig" };
            
            foreach (var key in requiredKeys)
            {
                if (!configuration.ContainsKey(key))
                {
                    _logger.LogError("Missing required configuration key: {Key}", key);
                    return false;
                }
            }

            // Validate blue environment configuration
            if (configuration["BlueEnvironment"] is not Dictionary<string, object> blueConfig)
            {
                _logger.LogError("Invalid BlueEnvironment configuration");
                return false;
            }

            // Validate green environment configuration
            if (configuration["GreenEnvironment"] is not Dictionary<string, object> greenConfig)
            {
                _logger.LogError("Invalid GreenEnvironment configuration");
                return false;
            }

            // Validate load balancer configuration
            if (configuration["LoadBalancerConfig"] is not Dictionary<string, object> lbConfig)
            {
                _logger.LogError("Invalid LoadBalancerConfig configuration");
                return false;
            }

            // Validate that load balancer has required endpoints
            if (!lbConfig.ContainsKey("Endpoint") || !lbConfig.ContainsKey("ApiKey"))
            {
                _logger.LogError("LoadBalancerConfig missing required Endpoint or ApiKey");
                return false;
            }

            _logger.LogInformation("Blue-green deployment configuration validated successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating blue-green deployment configuration");
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
            var serverCount = targetServers.Count;
            
            // Base time estimations for blue-green deployment phases
            var preDeploymentTime = TimeSpan.FromMinutes(5);
            var greenPreparationTime = TimeSpan.FromMinutes(10 + (serverCount / 2 * 2)); // 2 min per green server
            var greenDeploymentTime = TimeSpan.FromMinutes(15 + (serverCount / 2 * 3)); // 3 min per green server
            var greenValidationTime = TimeSpan.FromMinutes(10 + (serverCount / 2 * 1)); // 1 min per green server
            var trafficSwitchTime = TimeSpan.FromMinutes(5);
            var blueValidationTime = TimeSpan.FromMinutes(10);
            var cleanupTime = TimeSpan.FromMinutes(5 + (serverCount / 2 * 1)); // 1 min per blue server

            var totalTime = preDeploymentTime + greenPreparationTime + greenDeploymentTime + 
                          greenValidationTime + trafficSwitchTime + blueValidationTime + cleanupTime;

            // Add 20% buffer for blue-green deployments
            var bufferedTime = TimeSpan.FromMilliseconds(totalTime.TotalMilliseconds * 1.2);

            _logger.LogInformation("Estimated blue-green deployment time: {EstimatedTime} for {ServerCount} servers", 
                bufferedTime, serverCount);

            return bufferedTime;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error estimating blue-green deployment execution time");
            return TimeSpan.FromHours(2); // Default fallback
        }
    }

    private DeploymentPhase CreatePreDeploymentPhase(DeploymentWorkflowRequest request)
    {
        return new DeploymentPhase
        {
            Name = "Pre-Deployment Validation",
            Description = "Validate current environment and deployment prerequisites",
            TargetServers = request.TargetServers,
            Timeout = TimeSpan.FromMinutes(10),
            Steps = new List<DeploymentStep>
            {
                new DeploymentStep
                {
                    Name = "Validate Blue Environment",
                    Type = StepType.Validation,
                    TargetServer = "load-balancer",
                    Parameters = new Dictionary<string, object>
                    {
                        ["ValidationType"] = "BlueEnvironmentHealth",
                        ["Critical"] = true
                    }
                },
                new DeploymentStep
                {
                    Name = "Validate Green Environment Availability",
                    Type = StepType.Validation,
                    TargetServer = "load-balancer",
                    Parameters = new Dictionary<string, object>
                    {
                        ["ValidationType"] = "GreenEnvironmentAvailability",
                        ["Critical"] = true
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
                        ["Critical"] = false
                    }
                }
            }
        };
    }

    private DeploymentPhase CreateGreenEnvironmentPreparationPhase(DeploymentWorkflowRequest request, List<string> greenServers)
    {
        var steps = new List<DeploymentStep>();

        foreach (var server in greenServers)
        {
            steps.AddRange(new[]
            {
                new DeploymentStep
                {
                    Name = $"Stop Green Services on {server}",
                    Type = StepType.ServiceStop,
                    TargetServer = server,
                    Command = OrchestratorServiceCommand.Stop,
                    Parameters = new Dictionary<string, object>
                    {
                        ["ServiceName"] = request.ServiceName,
                        ["Critical"] = false
                    }
                },
                new DeploymentStep
                {
                    Name = $"Clean Green Environment on {server}",
                    Type = StepType.Cleanup,
                    TargetServer = server,
                    Parameters = new Dictionary<string, object>
                    {
                        ["CleanupType"] = "ServiceFiles",
                        ["ServiceName"] = request.ServiceName,
                        ["Critical"] = false
                    }
                }
            });
        }

        return new DeploymentPhase
        {
            Name = "Green Environment Preparation",
            Description = "Prepare green environment for new deployment",
            TargetServers = greenServers,
            Timeout = TimeSpan.FromMinutes(20),
            Steps = steps
        };
    }

    private DeploymentPhase CreateGreenDeploymentPhase(DeploymentWorkflowRequest request, List<string> greenServers)
    {
        var steps = new List<DeploymentStep>();

        foreach (var server in greenServers)
        {
            steps.AddRange(new[]
            {
                new DeploymentStep
                {
                    Name = $"Deploy to Green on {server}",
                    Type = StepType.Deploy,
                    TargetServer = server,
                    Parameters = new Dictionary<string, object>
                    {
                        ["PackageUrl"] = request.PackageUrl,
                        ["Version"] = request.Version,
                        ["ServiceName"] = request.ServiceName,
                        ["Environment"] = "Green",
                        ["Critical"] = true
                    }
                },
                new DeploymentStep
                {
                    Name = $"Start Green Service on {server}",
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
                    Name = $"Wait for Green Service Healthy on {server}",
                    Type = StepType.WaitForHealthy,
                    TargetServer = server,
                    Parameters = new Dictionary<string, object>
                    {
                        ["Timeout"] = TimeSpan.FromMinutes(5),
                        ["ServiceName"] = request.ServiceName,
                        ["Critical"] = true
                    }
                }
            });
        }

        return new DeploymentPhase
        {
            Name = "Green Environment Deployment",
            Description = "Deploy new version to green environment",
            TargetServers = greenServers,
            Timeout = TimeSpan.FromMinutes(30),
            Steps = steps
        };
    }

    private DeploymentPhase CreateGreenValidationPhase(DeploymentWorkflowRequest request, List<string> greenServers)
    {
        var steps = new List<DeploymentStep>
        {
            new DeploymentStep
            {
                Name = "Validate Green Environment Health",
                Type = StepType.HealthCheck,
                TargetServer = "health-check-service",
                Parameters = new Dictionary<string, object>
                {
                    ["TargetServers"] = greenServers,
                    ["ServiceName"] = request.ServiceName,
                    ["Environment"] = "Green",
                    ["Critical"] = true
                }
            },
            new DeploymentStep
            {
                Name = "Run Green Environment Smoke Tests",
                Type = StepType.Validation,
                TargetServer = "test-runner",
                Parameters = new Dictionary<string, object>
                {
                    ["TestType"] = "SmokeTests",
                    ["TargetServers"] = greenServers,
                    ["ServiceName"] = request.ServiceName,
                    ["Critical"] = true
                }
            },
            new DeploymentStep
            {
                Name = "Validate Green Service Endpoints",
                Type = StepType.Validation,
                TargetServer = "endpoint-validator",
                Parameters = new Dictionary<string, object>
                {
                    ["ValidationType"] = "EndpointConnectivity",
                    ["TargetServers"] = greenServers,
                    ["ServiceName"] = request.ServiceName,
                    ["Critical"] = true
                }
            }
        };

        return new DeploymentPhase
        {
            Name = "Green Environment Validation",
            Description = "Validate green environment is ready for traffic",
            TargetServers = greenServers,
            Timeout = TimeSpan.FromMinutes(15),
            Steps = steps
        };
    }

    private DeploymentPhase CreateTrafficSwitchPhase(DeploymentWorkflowRequest request, List<string> blueServers, List<string> greenServers)
    {
        return new DeploymentPhase
        {
            Name = "Traffic Switch",
            Description = "Switch traffic from blue to green environment",
            TargetServers = new List<string> { "load-balancer" },
            Timeout = TimeSpan.FromMinutes(10),
            Steps = new List<DeploymentStep>
            {
                new DeploymentStep
                {
                    Name = "Update Load Balancer Configuration",
                    Type = StepType.TrafficSwitch,
                    TargetServer = "load-balancer",
                    Parameters = new Dictionary<string, object>
                    {
                        ["Action"] = "SwitchToGreen",
                        ["BlueServers"] = blueServers,
                        ["GreenServers"] = greenServers,
                        ["ServiceName"] = request.ServiceName,
                        ["Critical"] = true
                    }
                },
                new DeploymentStep
                {
                    Name = "Validate Traffic Switch",
                    Type = StepType.Validation,
                    TargetServer = "traffic-validator",
                    Parameters = new Dictionary<string, object>
                    {
                        ["ValidationType"] = "TrafficRouting",
                        ["ExpectedTargets"] = greenServers,
                        ["ServiceName"] = request.ServiceName,
                        ["Critical"] = true
                    }
                },
                new DeploymentStep
                {
                    Name = "Monitor Green Environment Post-Switch",
                    Type = StepType.HealthCheck,
                    TargetServer = "monitoring-service",
                    Parameters = new Dictionary<string, object>
                    {
                        ["MonitorDuration"] = TimeSpan.FromMinutes(5),
                        ["TargetServers"] = greenServers,
                        ["ServiceName"] = request.ServiceName,
                        ["Critical"] = true
                    }
                }
            }
        };
    }

    private DeploymentPhase CreateBlueEnvironmentValidationPhase(DeploymentWorkflowRequest request, List<string> blueServers)
    {
        return new DeploymentPhase
        {
            Name = "Blue Environment Validation",
            Description = "Validate deployment success and blue environment status",
            TargetServers = blueServers,
            Timeout = TimeSpan.FromMinutes(15),
            Steps = new List<DeploymentStep>
            {
                new DeploymentStep
                {
                    Name = "Validate Green is Receiving Traffic",
                    Type = StepType.Validation,
                    TargetServer = "traffic-monitor",
                    Parameters = new Dictionary<string, object>
                    {
                        ["ValidationType"] = "TrafficFlow",
                        ["ServiceName"] = request.ServiceName,
                        ["ExpectedEnvironment"] = "Green",
                        ["Critical"] = true
                    }
                },
                new DeploymentStep
                {
                    Name = "Monitor System Performance",
                    Type = StepType.HealthCheck,
                    TargetServer = "performance-monitor",
                    Parameters = new Dictionary<string, object>
                    {
                        ["MonitorDuration"] = TimeSpan.FromMinutes(10),
                        ["ServiceName"] = request.ServiceName,
                        ["Critical"] = false
                    }
                },
                new DeploymentStep
                {
                    Name = "Update Deployment Status",
                    Type = StepType.Custom,
                    TargetServer = "deployment-tracker",
                    Parameters = new Dictionary<string, object>
                    {
                        ["Action"] = "UpdateStatus",
                        ["Status"] = "DeploymentComplete",
                        ["Environment"] = "Green",
                        ["ServiceName"] = request.ServiceName,
                        ["Version"] = request.Version,
                        ["Critical"] = false
                    }
                }
            }
        };
    }

    private DeploymentPhase CreatePostDeploymentCleanupPhase(DeploymentWorkflowRequest request, List<string> blueServers)
    {
        var steps = new List<DeploymentStep>
        {
            new DeploymentStep
            {
                Name = "Create Blue Environment Snapshot",
                Type = StepType.Custom,
                TargetServer = "backup-service",
                Parameters = new Dictionary<string, object>
                {
                    ["Action"] = "CreateSnapshot",
                    ["Environment"] = "Blue",
                    ["ServiceName"] = request.ServiceName,
                    ["TargetServers"] = blueServers,
                    ["Critical"] = false
                }
            }
        };

        // Add cleanup steps for each blue server
        foreach (var server in blueServers)
        {
            steps.AddRange(new[]
            {
                new DeploymentStep
                {
                    Name = $"Stop Blue Service on {server}",
                    Type = StepType.ServiceStop,
                    TargetServer = server,
                    Command = OrchestratorServiceCommand.Stop,
                    Parameters = new Dictionary<string, object>
                    {
                        ["ServiceName"] = request.ServiceName,
                        ["Critical"] = false
                    }
                },
                new DeploymentStep
                {
                    Name = $"Clean Blue Environment on {server}",
                    Type = StepType.Cleanup,
                    TargetServer = server,
                    Parameters = new Dictionary<string, object>
                    {
                        ["CleanupType"] = "OldVersionFiles",
                        ["ServiceName"] = request.ServiceName,
                        ["KeepSnapshots"] = true,
                        ["Critical"] = false
                    }
                }
            });
        }

        return new DeploymentPhase
        {
            Name = "Post-Deployment Cleanup",
            Description = "Clean up blue environment and finalize deployment",
            TargetServers = blueServers,
            Timeout = TimeSpan.FromMinutes(15),
            RollbackOnFailure = false, // Don't rollback during cleanup
            Steps = steps
        };
    }

    private List<string> GetBlueServers(List<string> targetServers, Dictionary<string, object> configuration)
    {
        if (configuration.TryGetValue("BlueEnvironment", out var blueConfigObj) &&
            blueConfigObj is Dictionary<string, object> blueConfig &&
            blueConfig.TryGetValue("Servers", out var blueServersObj) &&
            blueServersObj is List<string> blueServers)
        {
            return blueServers;
        }

        // Default: use even-indexed servers as blue
        return targetServers.Where((server, index) => index % 2 == 0).ToList();
    }

    private List<string> GetGreenServers(List<string> targetServers, Dictionary<string, object> configuration)
    {
        if (configuration.TryGetValue("GreenEnvironment", out var greenConfigObj) &&
            greenConfigObj is Dictionary<string, object> greenConfig &&
            greenConfig.TryGetValue("Servers", out var greenServersObj) &&
            greenServersObj is List<string> greenServers)
        {
            return greenServers;
        }

        // Default: use odd-indexed servers as green
        return targetServers.Where((server, index) => index % 2 == 1).ToList();
    }
}