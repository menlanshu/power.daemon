using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PowerDaemon.Central.Configuration;
using PowerDaemon.Central.Data;

namespace PowerDaemon.Central.Extensions;

public static class DatabaseExtensions
{
    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var databaseConfig = new DatabaseConfiguration();
        configuration.GetSection(DatabaseConfiguration.SectionName).Bind(databaseConfig);
        services.Configure<DatabaseConfiguration>(configuration.GetSection(DatabaseConfiguration.SectionName));

        services.AddDbContext<PowerDaemonContext>((serviceProvider, options) =>
        {
            ConfigureDatabase(options, databaseConfig);
        });

        return services;
    }

    private static void ConfigureDatabase(DbContextOptionsBuilder options, DatabaseConfiguration config)
    {
        switch (config.Provider.ToLowerInvariant())
        {
            case "postgresql":
                options.UseNpgsql(config.ConnectionString, npgsqlOptions =>
                {
                    npgsqlOptions.CommandTimeout(config.CommandTimeout);
                    npgsqlOptions.MigrationsAssembly(config.MigrationsAssembly);
                });
                break;

            // TODO: Add Oracle support in future phases
            // case "oracle":
            //     options.UseOracle(config.ConnectionString, oracleOptions =>
            //     {
            //         oracleOptions.CommandTimeout(config.CommandTimeout);
            //         oracleOptions.MigrationsAssembly(config.MigrationsAssembly);
            //     });
            //     break;

            default:
                throw new InvalidOperationException($"Unsupported database provider: {config.Provider}. Currently supported: PostgreSQL");
        }

        if (config.EnableSensitiveDataLogging)
        {
            options.EnableSensitiveDataLogging();
        }

        if (config.EnableDetailedErrors)
        {
            options.EnableDetailedErrors();
        }
    }

    public static async Task<IHost> MigrateDatabaseAsync(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILogger<PowerDaemonContext>>();
        var databaseConfig = services.GetRequiredService<IOptions<DatabaseConfiguration>>();

        try
        {
            if (databaseConfig.Value.AutoMigrateOnStartup)
            {
                logger.LogInformation("Starting database migration");
                var context = services.GetRequiredService<PowerDaemonContext>();
                await context.Database.MigrateAsync();
                logger.LogInformation("Database migration completed successfully");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while migrating the database");
            throw;
        }

        return host;
    }
}