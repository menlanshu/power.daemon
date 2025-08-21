using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PowerDaemon.Monitoring.Configuration;
using PowerDaemon.Monitoring.Services;
using PowerDaemon.Monitoring.Handlers;

namespace PowerDaemon.Monitoring.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMonitoring(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure monitoring settings
        services.Configure<MonitoringConfiguration>(configuration.GetSection("Monitoring"));

        // Register core monitoring services
        services.AddSingleton<IAlertService, AlertService>();
        services.AddSingleton<IAlertRuleService, AlertRuleService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IMetricsAggregationService, MetricsAggregationService>();
        services.AddSingleton<IMonitoringDashboardService, MonitoringDashboardService>();

        // Register background services
        services.AddHostedService<AlertEvaluationService>();
        services.AddHostedService<AlertCleanupService>();
        services.AddHostedService<NotificationRetryService>();

        // Register notification handlers
        services.AddNotificationHandlers();

        // Add HTTP client for webhook notifications
        services.AddHttpClient("PowerDaemon.Monitoring", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "PowerDaemon-Monitoring/1.0");
        });

        return services;
    }

    public static IServiceCollection AddNotificationHandlers(this IServiceCollection services)
    {
        // Register all notification handlers
        services.AddSingleton<INotificationHandler, EmailNotificationHandler>();
        services.AddSingleton<INotificationHandler, WebhookNotificationHandler>();
        services.AddSingleton<INotificationHandler, SlackNotificationHandler>();
        services.AddSingleton<INotificationHandler, TeamsNotificationHandler>();

        return services;
    }

    public static IServiceCollection AddMonitoringHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var healthChecksBuilder = services.AddHealthChecks();

        // Add monitoring-specific health checks
        healthChecksBuilder.AddCheck<AlertingHealthCheck>("alerting");
        healthChecksBuilder.AddCheck<NotificationHealthCheck>("notifications");
        healthChecksBuilder.AddCheck<MetricsHealthCheck>("metrics");

        return services;
    }

    public static IServiceCollection AddBuiltInAlertRules(this IServiceCollection services)
    {
        services.AddSingleton<IBuiltInAlertRules, BuiltInAlertRules>();
        return services;
    }
}