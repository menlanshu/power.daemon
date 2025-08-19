using PowerDaemon.Web.Components;
using PowerDaemon.Web.Hubs;
using PowerDaemon.Web.Services;
using PowerDaemon.Central.Data;
using PowerDaemon.Central.Extensions;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
builder.Host.UseSerilog((context, configuration) =>
{
    configuration.ReadFrom.Configuration(context.Configuration)
                 .WriteTo.Console()
                 .WriteTo.File("logs/web-.txt", rollingInterval: RollingInterval.Day);
});

// Add services to the container
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add SignalR
builder.Services.AddSignalR();

// Add database (shared with Central service)
builder.Services.AddDatabase(builder.Configuration);

// Add custom services
builder.Services.AddScoped<IRealTimeNotificationService, RealTimeNotificationService>();
builder.Services.AddScoped<IServiceManagementService, ServiceManagementService>();

// Add health checks
builder.Services.AddHealthChecks()
                .AddNpgSql(builder.Configuration.GetConnectionString("DefaultConnection") ?? 
                          builder.Configuration.GetSection("Database:ConnectionString").Value ?? 
                          "Host=localhost;Database=powerdaemon;Username=postgres;Password=password");

// Add Health Checks UI
builder.Services.AddHealthChecksUI(opt =>
{
    opt.SetEvaluationTimeInSeconds(15); // time in seconds between check
    opt.MaximumHistoryEntriesPerEndpoint(60); // maximum history of checks
    opt.SetApiMaxActiveRequests(1); // api requests concurrency
    opt.AddHealthCheckEndpoint("PowerDaemon API", "/health"); // map health check api
})
.AddInMemoryStorage();

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

// Map SignalR hub
app.MapHub<DashboardHub>("/dashboardhub");

// Map health checks
app.MapHealthChecks("/health");
app.MapHealthChecksUI();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

try
{
    Log.Information("PowerDaemon Web UI starting up");
    
    // Ensure database is migrated
    await app.MigrateDatabaseAsync();
    
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "PowerDaemon Web UI terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
