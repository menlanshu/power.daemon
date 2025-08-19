using PowerDaemon.Central.Data;
using PowerDaemon.Central.Extensions;
using PowerDaemon.Central.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
builder.Host.UseSerilog((context, configuration) =>
{
    configuration.ReadFrom.Configuration(context.Configuration)
                 .WriteTo.Console()
                 .WriteTo.File("logs/central-.txt", rollingInterval: RollingInterval.Day);
});

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { 
        Title = "PowerDaemon Central API", 
        Version = "v1",
        Description = "Enterprise Service Monitor & Deployment System - Central Service API"
    });
});

// Add database
builder.Services.AddDatabase(builder.Configuration);

// Add gRPC services
builder.Services.AddGrpc();

// Add health checks
builder.Services.AddHealthChecks()
                .AddNpgSql(builder.Configuration.GetConnectionString("DefaultConnection") ?? 
                          builder.Configuration.GetSection("Database:ConnectionString").Value ?? 
                          "Host=localhost;Database=powerdaemon;Username=postgres;Password=password");

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseRouting();

app.MapControllers();
app.MapHealthChecks("/health");

// Map gRPC services
app.MapGrpcService<AgentServiceImplementation>();

try
{
    Log.Information("PowerDaemon Central Service starting up");
    
    // Migrate database if configured
    await app.MigrateDatabaseAsync();
    
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
