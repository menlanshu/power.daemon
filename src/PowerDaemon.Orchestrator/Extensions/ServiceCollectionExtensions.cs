using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PowerDaemon.Orchestrator.Configuration;
using PowerDaemon.Orchestrator.Services;
using PowerDaemon.Orchestrator.Strategies;

namespace PowerDaemon.Orchestrator.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOrchestrator(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure orchestrator settings
        services.Configure<OrchestratorConfiguration>(options => configuration.GetSection("Orchestrator").Bind(options));

        // Register core orchestrator services
        services.AddSingleton<IDeploymentOrchestrator, DeploymentOrchestratorService>();
        services.AddSingleton<IWorkflowExecutor, WorkflowExecutor>();
        services.AddSingleton<IStrategyFactory, StrategyFactory>();

        // Register deployment strategies
        services.AddSingleton<IDeploymentStrategy, BlueGreenDeploymentStrategy>();
        services.AddSingleton<IDeploymentStrategy, CanaryDeploymentStrategy>();
        services.AddSingleton<IDeploymentStrategy, RollingDeploymentStrategy>();

        // Register repository and health services (these would be implemented based on your data layer)
        // services.AddScoped<IWorkflowRepository, WorkflowRepository>();
        // services.AddScoped<IHealthCheckService, HealthCheckService>();

        return services;
    }

    public static IServiceCollection AddDeploymentStrategies(this IServiceCollection services)
    {
        services.AddSingleton<IDeploymentStrategy, BlueGreenDeploymentStrategy>();
        services.AddSingleton<IDeploymentStrategy, CanaryDeploymentStrategy>();
        services.AddSingleton<IDeploymentStrategy, RollingDeploymentStrategy>();

        return services;
    }
}