using Microsoft.Extensions.Logging;
using PowerDaemon.Identity.Models;
using PowerDaemon.Cache.Services;

namespace PowerDaemon.Identity.Services;

public class RoleService : IRoleService
{
    private readonly ILogger<RoleService> _logger;
    private readonly ICacheService _cacheService;
    private readonly IPermissionService _permissionService;
    private static readonly List<Role> _builtInRoles = InitializeBuiltInRoles();

    public RoleService(
        ILogger<RoleService> logger,
        ICacheService cacheService,
        IPermissionService permissionService)
    {
        _logger = logger;
        _cacheService = cacheService;
        _permissionService = permissionService;
    }

    public async Task<Role?> GetRoleAsync(string roleId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check cache first
            var cacheKey = $"role:{roleId}";
            var cachedRole = await _cacheService.GetAsync<Role>(cacheKey);
            if (cachedRole != null)
            {
                return cachedRole;
            }

            // Check built-in roles
            var builtInRole = _builtInRoles.FirstOrDefault(r => r.Id == roleId || r.Name.Equals(roleId, StringComparison.OrdinalIgnoreCase));
            if (builtInRole != null)
            {
                // Enrich with permissions
                var enrichedRole = await EnrichRoleWithPermissions(builtInRole, cancellationToken);
                
                // Cache the enriched role
                await _cacheService.SetAsync(cacheKey, enrichedRole, TimeSpan.FromHours(1));
                return enrichedRole;
            }

            // Check custom roles (would be stored in database in a real implementation)
            var customRole = await GetCustomRoleAsync(roleId, cancellationToken);
            if (customRole != null)
            {
                await _cacheService.SetAsync(cacheKey, customRole, TimeSpan.FromHours(1));
                return customRole;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting role {RoleId}", roleId);
            return null;
        }
    }

    public async Task<List<Role>> GetAllRolesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var allRoles = new List<Role>();

            // Add built-in roles
            foreach (var builtInRole in _builtInRoles)
            {
                var enrichedRole = await EnrichRoleWithPermissions(builtInRole, cancellationToken);
                allRoles.Add(enrichedRole);
            }

            // Add custom roles
            var customRoles = await GetAllCustomRolesAsync(cancellationToken);
            allRoles.AddRange(customRoles);

            return allRoles.OrderBy(r => r.Priority).ThenBy(r => r.Name).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all roles");
            return new List<Role>();
        }
    }

    public async Task<List<Role>> SearchRolesAsync(RoleSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        try
        {
            var allRoles = await GetAllRolesAsync(cancellationToken);
            var filteredRoles = allRoles.AsEnumerable();

            // Apply filters
            if (!string.IsNullOrWhiteSpace(criteria.Name))
            {
                filteredRoles = filteredRoles.Where(r => 
                    r.Name.Contains(criteria.Name, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(criteria.DisplayName))
            {
                filteredRoles = filteredRoles.Where(r => 
                    r.DisplayName.Contains(criteria.DisplayName, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(criteria.Description))
            {
                filteredRoles = filteredRoles.Where(r => 
                    r.Description.Contains(criteria.Description, StringComparison.OrdinalIgnoreCase));
            }

            if (criteria.RoleType.HasValue)
            {
                filteredRoles = filteredRoles.Where(r => r.RoleType == criteria.RoleType.Value);
            }

            if (criteria.IsBuiltIn.HasValue)
            {
                filteredRoles = filteredRoles.Where(r => r.IsBuiltIn == criteria.IsBuiltIn.Value);
            }

            if (criteria.IsEnabled.HasValue)
            {
                filteredRoles = filteredRoles.Where(r => r.IsEnabled == criteria.IsEnabled.Value);
            }

            // Apply sorting
            var sortedRoles = criteria.SortBy?.ToLower() switch
            {
                "name" => criteria.SortDescending ? 
                    filteredRoles.OrderByDescending(r => r.Name) : 
                    filteredRoles.OrderBy(r => r.Name),
                "displayname" => criteria.SortDescending ? 
                    filteredRoles.OrderByDescending(r => r.DisplayName) : 
                    filteredRoles.OrderBy(r => r.DisplayName),
                "priority" => criteria.SortDescending ? 
                    filteredRoles.OrderByDescending(r => r.Priority) : 
                    filteredRoles.OrderBy(r => r.Priority),
                "createdat" => criteria.SortDescending ? 
                    filteredRoles.OrderByDescending(r => r.CreatedAt) : 
                    filteredRoles.OrderBy(r => r.CreatedAt),
                _ => filteredRoles.OrderBy(r => r.Priority).ThenBy(r => r.Name)
            };

            // Apply paging
            return sortedRoles.Skip(criteria.Skip).Take(criteria.Take).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching roles");
            return new List<Role>();
        }
    }

    public async Task<Role> CreateRoleAsync(CreateRoleRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Creating role {RoleName}", request.Name);

            // Validate role doesn't already exist
            var existingRole = await GetRoleAsync(request.Name, cancellationToken);
            if (existingRole != null)
            {
                throw new InvalidOperationException($"Role '{request.Name}' already exists");
            }

            var role = new Role
            {
                Id = Guid.NewGuid().ToString(),
                Name = request.Name,
                DisplayName = request.DisplayName,
                Description = request.Description,
                RoleType = request.RoleType,
                IsBuiltIn = false,
                IsEnabled = request.IsEnabled,
                Priority = request.Priority,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = "System", // Would be set to current user in real implementation
                UpdatedBy = "System"
            };

            // Add permissions
            foreach (var permissionId in request.PermissionIds)
            {
                var permission = await _permissionService.GetPermissionAsync(permissionId, cancellationToken);
                if (permission != null)
                {
                    role.Permissions.Add(permission);
                }
            }

            // Store role (in a real implementation, this would be stored in database)
            await StoreCustomRoleAsync(role, cancellationToken);

            // Cache the role
            var cacheKey = $"role:{role.Id}";
            await _cacheService.SetAsync(cacheKey, role, TimeSpan.FromHours(1));

            // Invalidate all roles cache
            await _cacheService.DeleteByPatternAsync("roles:*");

            _logger.LogInformation("Role {RoleName} created with ID {RoleId}", request.Name, role.Id);
            return role;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating role {RoleName}", request.Name);
            throw;
        }
    }

    public async Task<Role> UpdateRoleAsync(UpdateRoleRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Updating role {RoleId}", request.RoleId);

            var role = await GetRoleAsync(request.RoleId, cancellationToken);
            if (role == null)
            {
                throw new InvalidOperationException($"Role '{request.RoleId}' not found");
            }

            if (role.IsBuiltIn)
            {
                throw new InvalidOperationException("Built-in roles cannot be modified");
            }

            // Update properties
            if (!string.IsNullOrWhiteSpace(request.DisplayName))
                role.DisplayName = request.DisplayName;
            
            if (!string.IsNullOrWhiteSpace(request.Description))
                role.Description = request.Description;
            
            if (request.Priority.HasValue)
                role.Priority = request.Priority.Value;
            
            if (request.IsEnabled.HasValue)
                role.IsEnabled = request.IsEnabled.Value;

            role.UpdatedAt = DateTime.UtcNow;
            role.UpdatedBy = "System"; // Would be set to current user

            // Update permissions
            if (request.PermissionIds != null)
            {
                role.Permissions.Clear();
                foreach (var permissionId in request.PermissionIds)
                {
                    var permission = await _permissionService.GetPermissionAsync(permissionId, cancellationToken);
                    if (permission != null)
                    {
                        role.Permissions.Add(permission);
                    }
                }
            }

            // Update stored role
            await StoreCustomRoleAsync(role, cancellationToken);

            // Update cache
            var cacheKey = $"role:{role.Id}";
            await _cacheService.SetAsync(cacheKey, role, TimeSpan.FromHours(1));

            // Invalidate all roles cache
            await _cacheService.DeleteByPatternAsync("roles:*");

            _logger.LogInformation("Role {RoleId} updated", request.RoleId);
            return role;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating role {RoleId}", request.RoleId);
            throw;
        }
    }

    public async Task<bool> DeleteRoleAsync(string roleId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Deleting role {RoleId}", roleId);

            var role = await GetRoleAsync(roleId, cancellationToken);
            if (role == null)
            {
                return false;
            }

            if (role.IsBuiltIn)
            {
                throw new InvalidOperationException("Built-in roles cannot be deleted");
            }

            // Remove stored role
            await RemoveCustomRoleAsync(roleId, cancellationToken);

            // Remove from cache
            var cacheKey = $"role:{roleId}";
            await _cacheService.DeleteAsync(cacheKey);

            // Invalidate all roles cache
            await _cacheService.DeleteByPatternAsync("roles:*");

            _logger.LogInformation("Role {RoleId} deleted", roleId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting role {RoleId}", roleId);
            return false;
        }
    }

    public async Task<List<Permission>> GetRolePermissionsAsync(string roleId, CancellationToken cancellationToken = default)
    {
        try
        {
            var role = await GetRoleAsync(roleId, cancellationToken);
            return role?.Permissions ?? new List<Permission>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting permissions for role {RoleId}", roleId);
            return new List<Permission>();
        }
    }

    public async Task<bool> AssignPermissionToRoleAsync(string roleId, string permissionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var role = await GetRoleAsync(roleId, cancellationToken);
            if (role == null || role.IsBuiltIn)
            {
                return false;
            }

            var permission = await _permissionService.GetPermissionAsync(permissionId, cancellationToken);
            if (permission == null)
            {
                return false;
            }

            if (!role.Permissions.Any(p => p.Id == permissionId))
            {
                role.Permissions.Add(permission);
                role.UpdatedAt = DateTime.UtcNow;
                
                await StoreCustomRoleAsync(role, cancellationToken);
                
                // Update cache
                var cacheKey = $"role:{roleId}";
                await _cacheService.SetAsync(cacheKey, role, TimeSpan.FromHours(1));
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning permission {PermissionId} to role {RoleId}", permissionId, roleId);
            return false;
        }
    }

    public async Task<bool> RemovePermissionFromRoleAsync(string roleId, string permissionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var role = await GetRoleAsync(roleId, cancellationToken);
            if (role == null || role.IsBuiltIn)
            {
                return false;
            }

            var permissionToRemove = role.Permissions.FirstOrDefault(p => p.Id == permissionId);
            if (permissionToRemove != null)
            {
                role.Permissions.Remove(permissionToRemove);
                role.UpdatedAt = DateTime.UtcNow;
                
                await StoreCustomRoleAsync(role, cancellationToken);
                
                // Update cache
                var cacheKey = $"role:{roleId}";
                await _cacheService.SetAsync(cacheKey, role, TimeSpan.FromHours(1));
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing permission {PermissionId} from role {RoleId}", permissionId, roleId);
            return false;
        }
    }

    public async Task<List<User>> GetUsersWithRoleAsync(string roleId, CancellationToken cancellationToken = default)
    {
        // This would require querying users and checking their roles
        // For now, return empty list as this would need integration with user service
        return new List<User>();
    }

    public async Task<List<Group>> GetGroupsWithRoleAsync(string roleId, CancellationToken cancellationToken = default)
    {
        // This would require querying groups and checking their roles
        // For now, return empty list as this would need integration with group service
        return new List<Group>();
    }

    public async Task<bool> AssignRoleToUserAsync(string roleId, string userId, CancellationToken cancellationToken = default)
    {
        // This would be handled by the UserService
        throw new NotImplementedException("Role assignment to users is handled by UserService");
    }

    public async Task<bool> RemoveRoleFromUserAsync(string roleId, string userId, CancellationToken cancellationToken = default)
    {
        // This would be handled by the UserService
        throw new NotImplementedException("Role removal from users is handled by UserService");
    }

    private async Task<Role> EnrichRoleWithPermissions(Role role, CancellationToken cancellationToken)
    {
        try
        {
            // Clone the role to avoid modifying the original
            var enrichedRole = new Role
            {
                Id = role.Id,
                Name = role.Name,
                DisplayName = role.DisplayName,
                Description = role.Description,
                RoleType = role.RoleType,
                IsBuiltIn = role.IsBuiltIn,
                IsEnabled = role.IsEnabled,
                Priority = role.Priority,
                CreatedAt = role.CreatedAt,
                UpdatedAt = role.UpdatedAt,
                CreatedBy = role.CreatedBy,
                UpdatedBy = role.UpdatedBy,
                Permissions = new List<Permission>(role.Permissions)
            };

            // Get role-specific permissions from permission service
            var rolePermissions = await GetRoleSpecificPermissions(role.Name, cancellationToken);
            foreach (var permission in rolePermissions)
            {
                if (!enrichedRole.Permissions.Any(p => p.Id == permission.Id))
                {
                    enrichedRole.Permissions.Add(permission);
                }
            }

            return enrichedRole;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enriching role {RoleId} with permissions", role.Id);
            return role;
        }
    }

    private async Task<List<Permission>> GetRoleSpecificPermissions(string roleName, CancellationToken cancellationToken)
    {
        // Get permissions specific to this role from the permission service
        var allPermissions = await _permissionService.GetAllPermissionsAsync(cancellationToken);
        
        // Filter permissions that should be assigned to this role based on naming convention
        return allPermissions.Where(p => ShouldPermissionBeAssignedToRole(p, roleName)).ToList();
    }

    private static bool ShouldPermissionBeAssignedToRole(Permission permission, string roleName)
    {
        // Define role-permission mappings
        var rolePermissionMappings = new Dictionary<string, string[]>
        {
            ["Administrator"] = new[] { ".*" }, // Administrators get all permissions
            ["DeploymentManager"] = new[] { "deployment\\..*", "service\\.view", "server\\.view", "metrics\\.view" },
            ["ServiceManager"] = new[] { "service\\..*", "server\\.view", "metrics\\.view" },
            ["ServerManager"] = new[] { "server\\..*", "service\\.view", "metrics\\.view" },
            ["Operator"] = new[] { "service\\.view", "service\\.start", "service\\.stop", "service\\.restart", "server\\.view", "metrics\\.view" },
            ["MonitoringUser"] = new[] { "metrics\\..*", "server\\.view", "service\\.view" },
            ["Viewer"] = new[] { ".*\\.view" },
            ["Auditor"] = new[] { "audit\\..*", ".*\\.view" }
        };

        if (!rolePermissionMappings.TryGetValue(roleName, out var patterns))
        {
            return false;
        }

        var permissionName = $"{permission.Resource}.{permission.Action}";
        
        return patterns.Any(pattern => 
            System.Text.RegularExpressions.Regex.IsMatch(permissionName, pattern, 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }

    private async Task<Role?> GetCustomRoleAsync(string roleId, CancellationToken cancellationToken)
    {
        // In a real implementation, this would query the database
        // For now, return null as custom roles would be stored elsewhere
        return null;
    }

    private async Task<List<Role>> GetAllCustomRolesAsync(CancellationToken cancellationToken)
    {
        // In a real implementation, this would query the database
        // For now, return empty list as custom roles would be stored elsewhere
        return new List<Role>();
    }

    private async Task StoreCustomRoleAsync(Role role, CancellationToken cancellationToken)
    {
        // In a real implementation, this would save to database
        // For now, just log the operation
        _logger.LogDebug("Storing custom role {RoleId} (placeholder)", role.Id);
    }

    private async Task RemoveCustomRoleAsync(string roleId, CancellationToken cancellationToken)
    {
        // In a real implementation, this would delete from database
        // For now, just log the operation
        _logger.LogDebug("Removing custom role {RoleId} (placeholder)", roleId);
    }

    private static List<Role> InitializeBuiltInRoles()
    {
        return new List<Role>
        {
            new()
            {
                Id = "admin",
                Name = "Administrator",
                DisplayName = "Administrator",
                Description = "Full system access with all permissions",
                RoleType = RoleType.System,
                IsBuiltIn = true,
                IsEnabled = true,
                Priority = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = "System",
                UpdatedBy = "System",
                Permissions = new List<Permission>()
            },
            new()
            {
                Id = "deployment-manager",
                Name = "DeploymentManager",
                DisplayName = "Deployment Manager",
                Description = "Can create, execute, and manage deployments",
                RoleType = RoleType.Application,
                IsBuiltIn = true,
                IsEnabled = true,
                Priority = 2,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = "System",
                UpdatedBy = "System",
                Permissions = new List<Permission>()
            },
            new()
            {
                Id = "service-manager",
                Name = "ServiceManager",
                DisplayName = "Service Manager",
                Description = "Can manage services and view server information",
                RoleType = RoleType.Application,
                IsBuiltIn = true,
                IsEnabled = true,
                Priority = 3,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = "System",
                UpdatedBy = "System",
                Permissions = new List<Permission>()
            },
            new()
            {
                Id = "server-manager",
                Name = "ServerManager",
                DisplayName = "Server Manager",
                Description = "Can manage servers and view service information",
                RoleType = RoleType.Application,
                IsBuiltIn = true,
                IsEnabled = true,
                Priority = 4,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = "System",
                UpdatedBy = "System",
                Permissions = new List<Permission>()
            },
            new()
            {
                Id = "operator",
                Name = "Operator",
                DisplayName = "Operator",
                Description = "Can start, stop, and restart services, and view system information",
                RoleType = RoleType.Application,
                IsBuiltIn = true,
                IsEnabled = true,
                Priority = 5,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = "System",
                UpdatedBy = "System",
                Permissions = new List<Permission>()
            },
            new()
            {
                Id = "monitoring-user",
                Name = "MonitoringUser",
                DisplayName = "Monitoring User",
                Description = "Can view metrics and monitoring information",
                RoleType = RoleType.Application,
                IsBuiltIn = true,
                IsEnabled = true,
                Priority = 6,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = "System",
                UpdatedBy = "System",
                Permissions = new List<Permission>()
            },
            new()
            {
                Id = "viewer",
                Name = "Viewer",
                DisplayName = "Viewer",
                Description = "Read-only access to system information",
                RoleType = RoleType.Application,
                IsBuiltIn = true,
                IsEnabled = true,
                Priority = 7,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = "System",
                UpdatedBy = "System",
                Permissions = new List<Permission>()
            },
            new()
            {
                Id = "auditor",
                Name = "Auditor",
                DisplayName = "Auditor",
                Description = "Can view audit logs and system information for compliance",
                RoleType = RoleType.Application,
                IsBuiltIn = true,
                IsEnabled = true,
                Priority = 8,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = "System",
                UpdatedBy = "System",
                Permissions = new List<Permission>()
            }
        };
    }
}