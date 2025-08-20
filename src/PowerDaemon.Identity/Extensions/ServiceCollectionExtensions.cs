using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using PowerDaemon.Identity.Configuration;
using PowerDaemon.Identity.Services;
using System.Text;

namespace PowerDaemon.Identity.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPowerDaemonIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure Active Directory settings
        services.Configure<ActiveDirectoryConfiguration>(configuration.GetSection("ActiveDirectory"));
        
        // Configure JWT settings
        services.Configure<JwtConfiguration>(configuration.GetSection("Jwt"));

        // Register core identity services
        services.AddSingleton<IActiveDirectoryService, ActiveDirectoryService>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IGroupService, GroupService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IPermissionService, PermissionServiceSimple>();

        return services;
    }

    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtConfig = configuration.GetSection("Jwt").Get<JwtConfiguration>();
        if (jwtConfig == null)
        {
            throw new InvalidOperationException("JWT configuration is required");
        }

        if (string.IsNullOrEmpty(jwtConfig.SecretKey))
        {
            throw new InvalidOperationException("JWT SecretKey is required");
        }

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = false; // Set to true in production
            options.SaveToken = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = jwtConfig.ValidateIssuer,
                ValidIssuer = jwtConfig.Issuer,
                ValidateAudience = jwtConfig.ValidateAudience,
                ValidAudience = jwtConfig.Audience,
                ValidateLifetime = jwtConfig.ValidateLifetime,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtConfig.SecretKey)),
                ValidateIssuerSigningKey = jwtConfig.ValidateIssuerSigningKey,
                RequireExpirationTime = jwtConfig.RequireExpirationTime,
                RequireSignedTokens = jwtConfig.RequireSignedTokens,
                ClockSkew = jwtConfig.ClockSkew
            };

            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<JwtBearerEvents>>();
                    logger.LogError(context.Exception, "JWT authentication failed");
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<JwtBearerEvents>>();
                    var userName = context.Principal?.Identity?.Name;
                    logger.LogDebug("JWT token validated for user: {UserName}", userName);
                    return Task.CompletedTask;
                },
                OnChallenge = context =>
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<JwtBearerEvents>>();
                    logger.LogDebug("JWT authentication challenge: {Error} - {ErrorDescription}", context.Error, context.ErrorDescription);
                    return Task.CompletedTask;
                }
            };
        });

        return services;
    }

    public static IServiceCollection AddActiveDirectoryHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<ActiveDirectoryHealthCheck>("active_directory", tags: new[] { "active_directory", "external" });

        return services;
    }

    public static IServiceCollection AddIdentityPolicies(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            // Built-in policies
            options.AddPolicy("RequireAdministratorRole", policy =>
                policy.RequireRole("Administrator", "Admin"));

            options.AddPolicy("RequireOperatorRole", policy =>
                policy.RequireRole("Operator", "PowerUser", "Administrator", "Admin"));

            options.AddPolicy("RequireViewerRole", policy =>
                policy.RequireRole("Viewer", "Operator", "PowerUser", "Administrator", "Admin"));

            // Deployment policies
            options.AddPolicy("CanCreateDeployment", policy =>
                policy.RequireAssertion(context =>
                    context.User.IsInRole("Administrator") ||
                    context.User.IsInRole("DeploymentManager") ||
                    context.User.HasClaim("permission", "deployment.create")));

            options.AddPolicy("CanExecuteDeployment", policy =>
                policy.RequireAssertion(context =>
                    context.User.IsInRole("Administrator") ||
                    context.User.IsInRole("DeploymentManager") ||
                    context.User.IsInRole("Operator") ||
                    context.User.HasClaim("permission", "deployment.execute")));

            options.AddPolicy("CanCancelDeployment", policy =>
                policy.RequireAssertion(context =>
                    context.User.IsInRole("Administrator") ||
                    context.User.IsInRole("DeploymentManager") ||
                    context.User.HasClaim("permission", "deployment.cancel")));

            // Service management policies
            options.AddPolicy("CanManageServices", policy =>
                policy.RequireAssertion(context =>
                    context.User.IsInRole("Administrator") ||
                    context.User.IsInRole("ServiceManager") ||
                    context.User.HasClaim("permission", "service.manage")));

            options.AddPolicy("CanViewServices", policy =>
                policy.RequireAssertion(context =>
                    context.User.IsInRole("Administrator") ||
                    context.User.IsInRole("ServiceManager") ||
                    context.User.IsInRole("Operator") ||
                    context.User.IsInRole("Viewer") ||
                    context.User.HasClaim("permission", "service.view")));

            // Server management policies
            options.AddPolicy("CanManageServers", policy =>
                policy.RequireAssertion(context =>
                    context.User.IsInRole("Administrator") ||
                    context.User.IsInRole("ServerManager") ||
                    context.User.HasClaim("permission", "server.manage")));

            options.AddPolicy("CanViewServers", policy =>
                policy.RequireAssertion(context =>
                    context.User.IsInRole("Administrator") ||
                    context.User.IsInRole("ServerManager") ||
                    context.User.IsInRole("Operator") ||
                    context.User.IsInRole("Viewer") ||
                    context.User.HasClaim("permission", "server.view")));

            // Monitoring policies
            options.AddPolicy("CanViewMetrics", policy =>
                policy.RequireAssertion(context =>
                    context.User.IsInRole("Administrator") ||
                    context.User.IsInRole("MonitoringUser") ||
                    context.User.IsInRole("Operator") ||
                    context.User.IsInRole("Viewer") ||
                    context.User.HasClaim("permission", "metrics.view")));

            // Configuration policies
            options.AddPolicy("CanManageConfiguration", policy =>
                policy.RequireAssertion(context =>
                    context.User.IsInRole("Administrator") ||
                    context.User.HasClaim("permission", "configuration.manage")));

            // User management policies
            options.AddPolicy("CanManageUsers", policy =>
                policy.RequireAssertion(context =>
                    context.User.IsInRole("Administrator") ||
                    context.User.IsInRole("UserManager") ||
                    context.User.HasClaim("permission", "user.manage")));

            // Audit policies
            options.AddPolicy("CanViewAuditLogs", policy =>
                policy.RequireAssertion(context =>
                    context.User.IsInRole("Administrator") ||
                    context.User.IsInRole("Auditor") ||
                    context.User.HasClaim("permission", "audit.view")));
        });

        return services;
    }
}

public class ActiveDirectoryHealthCheck : IHealthCheck
{
    private readonly IActiveDirectoryService _activeDirectoryService;
    private readonly ILogger<ActiveDirectoryHealthCheck> _logger;

    public ActiveDirectoryHealthCheck(
        IActiveDirectoryService activeDirectoryService,
        ILogger<ActiveDirectoryHealthCheck> logger)
    {
        _activeDirectoryService = activeDirectoryService;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var isConnected = await _activeDirectoryService.TestConnectionAsync(cancellationToken);
            
            if (isConnected)
            {
                var connectionInfo = await _activeDirectoryService.GetConnectionInfoAsync(cancellationToken);
                
                return HealthCheckResult.Healthy("Active Directory connection is healthy", connectionInfo);
            }
            else
            {
                return HealthCheckResult.Unhealthy("Active Directory connection failed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Active Directory health check failed");
            return HealthCheckResult.Unhealthy("Active Directory health check failed", ex);
        }
    }
}
