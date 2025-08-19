using PowerDaemon.Agent;
using PowerDaemon.Agent.Services;
using PowerDaemon.Shared.Configuration;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// Configure Serilog
builder.Services.AddSerilog(config =>
{
    config.ReadFrom.Configuration(builder.Configuration)
          .WriteTo.Console()
          .WriteTo.File("logs/agent-.txt", rollingInterval: RollingInterval.Day);
});

// Configure services
builder.Services.Configure<AgentConfiguration>(
    builder.Configuration.GetSection(AgentConfiguration.SectionName));

// Register services
builder.Services.AddSingleton<IServiceDiscovery, ServiceDiscoveryService>();
builder.Services.AddSingleton<IMetricsCollector, MetricsCollectorService>();
builder.Services.AddSingleton<IGrpcClient, GrpcClientService>();

// Register the main worker
builder.Services.AddHostedService<Worker>();

// Configure as Windows Service or Linux systemd daemon
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "PowerDaemon Agent";
});

builder.Services.AddSystemd();

var host = builder.Build();

try
{
    Log.Information("PowerDaemon Agent starting up");
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
