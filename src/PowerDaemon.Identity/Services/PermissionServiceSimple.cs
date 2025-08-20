using Microsoft.Extensions.Logging;
using PowerDaemon.Identity.Models;
using PowerDaemon.Cache.Services;

namespace PowerDaemon.Identity.Services;

public class PermissionServiceSimple : IPermissionService
{
    private readonly ILogger<PermissionServiceSimple> _logger;
    private readonly ICacheService _cacheService;
    private static readonly List<Permission> _builtInPermissions = InitializeBuiltInPermissions();

    public PermissionServiceSimple(
        ILogger<PermissionServiceSimple> logger,
        ICacheService cacheService)
    {
        _logger = logger;
        _cacheService = cacheService;
    }

    public async Task<Permission?> GetPermissionAsync(string permissionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var permission = _builtInPermissions.FirstOrDefault(p => 
                p.Id == permissionId || 
                $"{p.Resource}.{p.Action}".Equals(permissionId, StringComparison.OrdinalIgnoreCase));
            
            return permission;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting permission {PermissionId}", permissionId);
            return null;
        }
    }

    public async Task<List<Permission>> GetAllPermissionsAsync(CancellationToken cancellationToken = default)
    {
        return _builtInPermissions.OrderBy(p => p.Resource).ThenBy(p => p.Action).ToList();
    }

    public async Task<List<Permission>> SearchPermissionsAsync(PermissionSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        var allPermissions = await GetAllPermissionsAsync(cancellationToken);
        var filteredPermissions = allPermissions.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(criteria.Resource))
        {
            filteredPermissions = filteredPermissions.Where(p =>
                p.Resource.Contains(criteria.Resource, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(criteria.Action))
        {
            filteredPermissions = filteredPermissions.Where(p =>
                p.Action.Contains(criteria.Action, StringComparison.OrdinalIgnoreCase));
        }

        return filteredPermissions.Skip(criteria.Skip).Take(criteria.Take).ToList();
    }

    public async Task<bool> EvaluatePermissionAsync(string userId, string resource, string action, Dictionary<string, object>? context = null, CancellationToken cancellationToken = default)
    {
        try
        {
            // Simple permission check - would be more complex in real implementation
            var permission = await GetPermissionAsync($"{resource}.{action}", cancellationToken);
            return permission != null && permission.IsEnabled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating permission for user {UserId}: {Resource}.{Action}", userId, resource, action);
            return false;
        }
    }

    public async Task<List<Permission>> GetEffectivePermissionsAsync(string userId, string? resource = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var permissions = await GetAllPermissionsAsync(cancellationToken);
            
            if (!string.IsNullOrWhiteSpace(resource))
            {
                permissions = permissions.Where(p => 
                    p.Resource.Equals(resource, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            return permissions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting effective permissions for user {UserId}", userId);
            return new List<Permission>();
        }
    }

    public async Task<PermissionEvaluationResult> EvaluatePermissionsAsync(PermissionEvaluationRequest request, CancellationToken cancellationToken = default)
    {
        return new PermissionEvaluationResult
        {
            UserId = request.UserId,
            Results = new List<PermissionCheckResult>(),
            AllGranted = false,
            EvaluatedAt = DateTime.UtcNow
        };
    }

    public async Task<List<Permission>> GetChildPermissionsAsync(string permissionId, CancellationToken cancellationToken = default)
    {
        return new List<Permission>();
    }

    public async Task<List<Permission>> GetParentPermissionsAsync(string permissionId, CancellationToken cancellationToken = default)
    {
        return new List<Permission>();
    }

    public async Task<Permission> CreatePermissionAsync(CreatePermissionRequest request, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Custom permission creation not implemented in this version");
    }

    public async Task<Permission> UpdatePermissionAsync(UpdatePermissionRequest request, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Custom permission updates not implemented in this version");
    }

    public async Task<bool> DeletePermissionAsync(string permissionId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Custom permission deletion not implemented in this version");
    }

    public async Task<List<Permission>> GetPermissionsByResourceAsync(string resource, CancellationToken cancellationToken = default)
    {
        var allPermissions = await GetAllPermissionsAsync(cancellationToken);
        return allPermissions.Where(p => p.Resource.Equals(resource, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public async Task<List<string>> GetAvailableResourcesAsync(CancellationToken cancellationToken = default)
    {
        var allPermissions = await GetAllPermissionsAsync(cancellationToken);
        return allPermissions.Select(p => p.Resource).Distinct().OrderBy(r => r).ToList();
    }

    public async Task<List<string>> GetAvailableActionsAsync(string? resource = null, CancellationToken cancellationToken = default)
    {
        var allPermissions = await GetAllPermissionsAsync(cancellationToken);
        
        if (!string.IsNullOrWhiteSpace(resource))
        {
            allPermissions = allPermissions.Where(p => p.Resource.Equals(resource, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return allPermissions.Select(p => p.Action).Distinct().OrderBy(a => a).ToList();
    }

    private static List<Permission> InitializeBuiltInPermissions()
    {
        var permissions = new List<Permission>();
        
        // System permissions
        AddPermission(permissions, "system", "view", "View System Information", "View general system information");
        AddPermission(permissions, "system", "manage", "Manage System", "Full system management access");
        
        // Deployment permissions
        AddPermission(permissions, "deployment", "view", "View Deployments", "View deployment information");
        AddPermission(permissions, "deployment", "create", "Create Deployments", "Create new deployments");
        AddPermission(permissions, "deployment", "execute", "Execute Deployments", "Execute deployments");
        
        // Service permissions
        AddPermission(permissions, "service", "view", "View Services", "View service information");
        AddPermission(permissions, "service", "manage", "Manage Services", "Full service management");
        AddPermission(permissions, "service", "start", "Start Services", "Start services");
        AddPermission(permissions, "service", "stop", "Stop Services", "Stop services");
        
        // Server permissions
        AddPermission(permissions, "server", "view", "View Servers", "View server information");
        AddPermission(permissions, "server", "manage", "Manage Servers", "Full server management");
        
        return permissions;
    }

    private static void AddPermission(List<Permission> permissions, string resource, string action, string name, string description)
    {
        permissions.Add(new Permission
        {
            Id = $"{resource}.{action}",
            Resource = resource,
            Action = action,
            Name = name,
            Description = description,
            IsBuiltIn = true,
            IsEnabled = true
        });
    }
}