using Microsoft.Extensions.Logging;
using PowerDaemon.Identity.Models;
using PowerDaemon.Cache.Services;

namespace PowerDaemon.Identity.Services;

public class GroupService : IGroupService
{
    private readonly ILogger<GroupService> _logger;
    private readonly IActiveDirectoryService _activeDirectoryService;
    private readonly ICacheService _cacheService;
    private readonly IRoleService _roleService;
    private readonly IPermissionService _permissionService;

    public GroupService(
        ILogger<GroupService> logger,
        IActiveDirectoryService activeDirectoryService,
        ICacheService cacheService,
        IRoleService roleService,
        IPermissionService permissionService)
    {
        _logger = logger;
        _activeDirectoryService = activeDirectoryService;
        _cacheService = cacheService;
        _roleService = roleService;
        _permissionService = permissionService;
    }

    public async Task<Group?> GetGroupAsync(string groupId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check cache first
            var cacheKey = $"group:{groupId}";
            var cachedGroup = await _cacheService.GetAsync<Group>(cacheKey);
            if (cachedGroup != null)
            {
                return cachedGroup;
            }

            // Get from Active Directory
            var group = await _activeDirectoryService.GetGroupAsync(groupId, cancellationToken);
            if (group != null)
            {
                // Enrich with roles and permissions
                group = await EnrichGroupWithRolesAndPermissions(group, cancellationToken);

                // Cache the enriched group
                await _cacheService.SetAsync(cacheKey, group, TimeSpan.FromHours(1));
            }

            return group;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting group {GroupId}", groupId);
            return null;
        }
    }

    public async Task<List<Group>> SearchGroupsAsync(GroupSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Searching groups with criteria");

            var groups = await _activeDirectoryService.SearchGroupsAsync(
                BuildSearchFilter(criteria), 
                criteria.Take, 
                cancellationToken);

            // Apply additional filtering
            groups = ApplyGroupFilters(groups, criteria);

            // Enrich with roles and permissions
            var enrichedGroups = new List<Group>();
            foreach (var group in groups)
            {
                var enrichedGroup = await EnrichGroupWithRolesAndPermissions(group, cancellationToken);
                enrichedGroups.Add(enrichedGroup);
            }

            // Apply role-based filtering would go here if GroupSearchCriteria had Roles property
            // This is commented out as the property doesn't exist in the current model

            // Apply sorting and paging
            enrichedGroups = ApplySortingAndPaging(enrichedGroups, criteria);

            _logger.LogDebug("Found {Count} groups matching criteria", enrichedGroups.Count);
            return enrichedGroups;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching groups");
            return new List<Group>();
        }
    }

    public async Task<List<User>> GetGroupMembersAsync(string groupId, bool includeNestedGroups = true, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _activeDirectoryService.GetUsersInGroupAsync(groupId, includeNestedGroups, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting members for group {GroupId}", groupId);
            return new List<User>();
        }
    }

    public async Task<List<Group>> GetNestedGroupsAsync(string groupId, int maxDepth = 10, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _activeDirectoryService.GetNestedGroupsAsync(groupId, maxDepth, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting nested groups for {GroupId}", groupId);
            return new List<Group>();
        }
    }

    public async Task<bool> IsUserInGroupAsync(string userId, string groupId, bool includeNestedGroups = true, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _activeDirectoryService.IsUserInGroupAsync(userId, groupId, includeNestedGroups, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if user {UserId} is in group {GroupId}", userId, groupId);
            return false;
        }
    }

    public async Task<List<Role>> GetGroupRolesAsync(string groupId, CancellationToken cancellationToken = default)
    {
        try
        {
            var group = await GetGroupAsync(groupId, cancellationToken);
            return group?.Roles ?? new List<Role>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting roles for group {GroupId}", groupId);
            return new List<Role>();
        }
    }

    public async Task<List<Permission>> GetGroupPermissionsAsync(string groupId, string? resource = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var group = await GetGroupAsync(groupId, cancellationToken);
            if (group == null)
            {
                return new List<Permission>();
            }

            var permissions = new List<Permission>();
            
            // Get permissions from all group roles
            foreach (var role in group.Roles)
            {
                var rolePermissions = await _roleService.GetRolePermissionsAsync(role.Id, cancellationToken);
                permissions.AddRange(rolePermissions);
            }

            // Filter by resource if specified
            if (!string.IsNullOrWhiteSpace(resource))
            {
                permissions = permissions.Where(p => p.Resource.Equals(resource, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            // Remove duplicates
            permissions = permissions.GroupBy(p => new { p.Resource, p.Action })
                                   .Select(g => g.First())
                                   .ToList();

            return permissions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting permissions for group {GroupId}", groupId);
            return new List<Permission>();
        }
    }

    public async Task<bool> AssignRoleToGroupAsync(string groupId, string roleId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Assigning role {RoleId} to group {GroupId}", roleId, groupId);

            var group = await GetGroupAsync(groupId, cancellationToken);
            if (group == null)
            {
                _logger.LogWarning("Group {GroupId} not found", groupId);
                return false;
            }

            var role = await _roleService.GetRoleAsync(roleId, cancellationToken);
            if (role == null)
            {
                _logger.LogWarning("Role {RoleId} not found", roleId);
                return false;
            }

            // Check if role is already assigned
            if (group.Roles.Any(r => r.Id == roleId))
            {
                _logger.LogDebug("Role {RoleId} already assigned to group {GroupId}", roleId, groupId);
                return true;
            }

            // Add role to group
            group.Roles.Add(role);

            // Store the updated group role mapping (in a real implementation, this would be in database)
            await StoreGroupRoleMappingAsync(groupId, roleId, cancellationToken);

            // Invalidate cache
            var cacheKey = $"group:{groupId}";
            await _cacheService.DeleteAsync(cacheKey);

            _logger.LogInformation("Role {RoleId} assigned to group {GroupId}", roleId, groupId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning role {RoleId} to group {GroupId}", roleId, groupId);
            return false;
        }
    }

    public async Task<bool> RemoveRoleFromGroupAsync(string groupId, string roleId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Removing role {RoleId} from group {GroupId}", roleId, groupId);

            var group = await GetGroupAsync(groupId, cancellationToken);
            if (group == null)
            {
                return false;
            }

            var roleToRemove = group.Roles.FirstOrDefault(r => r.Id == roleId);
            if (roleToRemove == null)
            {
                _logger.LogDebug("Role {RoleId} not assigned to group {GroupId}", roleId, groupId);
                return true;
            }

            // Remove role from group
            group.Roles.Remove(roleToRemove);

            // Remove the group role mapping
            await RemoveGroupRoleMappingAsync(groupId, roleId, cancellationToken);

            // Invalidate cache
            var cacheKey = $"group:{groupId}";
            await _cacheService.DeleteAsync(cacheKey);

            _logger.LogInformation("Role {RoleId} removed from group {GroupId}", roleId, groupId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing role {RoleId} from group {GroupId}", roleId, groupId);
            return false;
        }
    }

    public async Task<bool> HasPermissionAsync(string groupId, string resource, string action, Dictionary<string, object>? context = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var permissions = await GetGroupPermissionsAsync(groupId, resource, cancellationToken);
            return permissions.Any(p => p.Resource.Equals(resource, StringComparison.OrdinalIgnoreCase) &&
                                       p.Action.Equals(action, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking permission for group {GroupId}", groupId);
            return false;
        }
    }

    public async Task<List<Group>> GetUserGroupsAsync(string userId, bool includeNestedGroups = true, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _activeDirectoryService.GetUserGroupsAsync(userId, includeNestedGroups, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting groups for user {UserId}", userId);
            return new List<Group>();
        }
    }

    public async Task<List<Group>> GetAllGroupsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // For performance, we'll search with a broad filter and cache the results
            var cacheKey = "groups:all";
            var cachedGroups = await _cacheService.GetAsync<List<Group>>(cacheKey);
            if (cachedGroups != null)
            {
                return cachedGroups;
            }

            var groups = await _activeDirectoryService.SearchGroupsAsync("*", 1000, cancellationToken);
            
            // Enrich with roles and permissions
            var enrichedGroups = new List<Group>();
            foreach (var group in groups)
            {
                var enrichedGroup = await EnrichGroupWithRolesAndPermissions(group, cancellationToken);
                enrichedGroups.Add(enrichedGroup);
            }

            // Cache for 30 minutes
            await _cacheService.SetAsync(cacheKey, enrichedGroups, TimeSpan.FromMinutes(30));
            
            return enrichedGroups;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all groups");
            return new List<Group>();
        }
    }

    public async Task<Group> CreateGroupAsync(CreateGroupRequest request, CancellationToken cancellationToken = default)
    {
        // Note: Creating groups in AD requires different approach and permissions
        throw new NotImplementedException("Group creation in Active Directory requires administrative access and is not implemented in this service");
    }

    public async Task<Group> UpdateGroupAsync(UpdateGroupRequest request, CancellationToken cancellationToken = default)
    {
        // Note: Updating groups in AD requires different approach and permissions
        throw new NotImplementedException("Group updates in Active Directory require administrative access and is not implemented in this service");
    }

    public async Task<bool> DeleteGroupAsync(string groupId, CancellationToken cancellationToken = default)
    {
        // Note: Deleting groups in AD requires different approach and permissions
        throw new NotImplementedException("Group deletion in Active Directory requires administrative access and is not implemented in this service");
    }

    public async Task<bool> SynchronizeGroupAsync(string groupId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Invalidate cache
            var cacheKey = $"group:{groupId}";
            await _cacheService.DeleteAsync(cacheKey);

            // Synchronize with AD
            await _activeDirectoryService.InvalidateGroupCacheAsync(groupId);

            // Fetch fresh data
            var group = await GetGroupAsync(groupId, cancellationToken);
            
            _logger.LogInformation("Group {GroupId} synchronized successfully", groupId);
            return group != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error synchronizing group {GroupId}", groupId);
            return false;
        }
    }

    private async Task<Group> EnrichGroupWithRolesAndPermissions(Group group, CancellationToken cancellationToken)
    {
        try
        {
            // Map group to roles based on naming conventions and stored mappings
            var roles = await GetRolesForGroup(group, cancellationToken);
            group.Roles = roles;

            // Get effective permissions from all roles
            var permissions = new List<Permission>();
            foreach (var role in roles)
            {
                var rolePermissions = await _roleService.GetRolePermissionsAsync(role.Id, cancellationToken);
                permissions.AddRange(rolePermissions);
            }

            // Remove duplicate permissions
            group.Permissions = permissions.GroupBy(p => new { p.Resource, p.Action })
                                          .Select(g => g.First())
                                          .ToList();

            return group;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error enriching group {GroupId} with roles and permissions", group.Id);
            return group;
        }
    }

    private async Task<List<Role>> GetRolesForGroup(Group group, CancellationToken cancellationToken)
    {
        var roles = new List<Role>();

        // Get stored group role mappings (would be from database in real implementation)
        var storedRoles = await GetStoredGroupRolesAsync(group.Id, cancellationToken);
        roles.AddRange(storedRoles);

        // Add default roles based on group naming patterns
        var defaultRoles = await GetDefaultRolesForGroup(group, cancellationToken);
        foreach (var role in defaultRoles)
        {
            if (!roles.Any(r => r.Id == role.Id))
            {
                roles.Add(role);
            }
        }

        return roles;
    }

    private async Task<List<Role>> GetDefaultRolesForGroup(Group group, CancellationToken cancellationToken)
    {
        var roles = new List<Role>();

        // Default role mapping based on common AD group patterns
        var groupNameRoleMap = new Dictionary<string, string[]>
        {
            ["Domain Admins"] = new[] { "Administrator" },
            ["Enterprise Admins"] = new[] { "Administrator" },
            ["PowerDaemon Admins"] = new[] { "Administrator" },
            ["PowerDaemon Operators"] = new[] { "Operator" },
            ["PowerDaemon Viewers"] = new[] { "Viewer" },
            ["Deployment Managers"] = new[] { "DeploymentManager" },
            ["Service Managers"] = new[] { "ServiceManager" },
            ["Server Managers"] = new[] { "ServerManager" },
            ["Monitoring Users"] = new[] { "MonitoringUser" },
            ["Auditors"] = new[] { "Auditor" }
        };

        // Check exact matches
        if (groupNameRoleMap.TryGetValue(group.Name, out var exactRoleNames))
        {
            foreach (var roleName in exactRoleNames)
            {
                var role = await _roleService.GetRoleAsync(roleName, cancellationToken);
                if (role != null)
                {
                    roles.Add(role);
                }
            }
        }
        else
        {
            // Check partial matches
            foreach (var kvp in groupNameRoleMap)
            {
                if (group.Name.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var roleName in kvp.Value)
                    {
                        var role = await _roleService.GetRoleAsync(roleName, cancellationToken);
                        if (role != null && !roles.Any(r => r.Id == role.Id))
                        {
                            roles.Add(role);
                        }
                    }
                }
            }
        }

        return roles;
    }

    private async Task<List<Role>> GetStoredGroupRolesAsync(string groupId, CancellationToken cancellationToken)
    {
        // In a real implementation, this would query the database for stored group-role mappings
        // For now, return empty list as this would be stored in database
        return new List<Role>();
    }

    private async Task StoreGroupRoleMappingAsync(string groupId, string roleId, CancellationToken cancellationToken)
    {
        // In a real implementation, this would store the group-role mapping in database
        // For now, just log the operation
        _logger.LogDebug("Storing group-role mapping: {GroupId} -> {RoleId} (placeholder)", groupId, roleId);
    }

    private async Task RemoveGroupRoleMappingAsync(string groupId, string roleId, CancellationToken cancellationToken)
    {
        // In a real implementation, this would remove the group-role mapping from database
        // For now, just log the operation
        _logger.LogDebug("Removing group-role mapping: {GroupId} -> {RoleId} (placeholder)", groupId, roleId);
    }

    private static string BuildSearchFilter(GroupSearchCriteria criteria)
    {
        var filters = new List<string>();

        if (!string.IsNullOrWhiteSpace(criteria.Name))
            filters.Add(criteria.Name);

        if (!string.IsNullOrWhiteSpace(criteria.DisplayName))
            filters.Add(criteria.DisplayName);

        if (!string.IsNullOrWhiteSpace(criteria.Description))
            filters.Add(criteria.Description);

        return string.Join(" ", filters);
    }

    private static List<Group> ApplyGroupFilters(List<Group> groups, GroupSearchCriteria criteria)
    {
        var filteredGroups = groups.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(criteria.Name))
        {
            filteredGroups = filteredGroups.Where(g =>
                g.Name.Contains(criteria.Name, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(criteria.DisplayName))
        {
            filteredGroups = filteredGroups.Where(g =>
                g.DisplayName.Contains(criteria.DisplayName, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(criteria.Description))
        {
            filteredGroups = filteredGroups.Where(g =>
                g.Description.Contains(criteria.Description, StringComparison.OrdinalIgnoreCase));
        }

        if (criteria.GroupType.HasValue)
        {
            filteredGroups = filteredGroups.Where(g => g.GroupType == criteria.GroupType.Value);
        }

        if (criteria.Scope.HasValue)
        {
            filteredGroups = filteredGroups.Where(g => g.Scope == criteria.Scope.Value);
        }

        if (criteria.IsEnabled.HasValue)
        {
            filteredGroups = filteredGroups.Where(g => g.IsEnabled == criteria.IsEnabled.Value);
        }

        return filteredGroups.ToList();
    }

    public async Task<bool> AddUserToGroupAsync(string groupId, string userId, CancellationToken cancellationToken = default)
    {
        // Note: Adding users to AD groups requires different approach and permissions
        throw new NotImplementedException("Adding users to Active Directory groups requires administrative access and is not implemented in this service");
    }

    public async Task<bool> RemoveUserFromGroupAsync(string groupId, string userId, CancellationToken cancellationToken = default)
    {
        // Note: Removing users from AD groups requires different approach and permissions
        throw new NotImplementedException("Removing users from Active Directory groups requires administrative access and is not implemented in this service");
    }

    public async Task<bool> AddNestedGroupAsync(string parentGroupId, string childGroupId, CancellationToken cancellationToken = default)
    {
        // Note: Adding nested groups in AD requires different approach and permissions
        throw new NotImplementedException("Adding nested groups in Active Directory requires administrative access and is not implemented in this service");
    }

    public async Task<bool> RemoveNestedGroupAsync(string parentGroupId, string childGroupId, CancellationToken cancellationToken = default)
    {
        // Note: Removing nested groups in AD requires different approach and permissions
        throw new NotImplementedException("Removing nested groups from Active Directory requires administrative access and is not implemented in this service");
    }

    private static List<Group> ApplySortingAndPaging(List<Group> groups, GroupSearchCriteria criteria)
    {
        // Apply sorting
        var sortedGroups = criteria.SortBy?.ToLower() switch
        {
            "name" => criteria.SortDescending ?
                groups.OrderByDescending(g => g.Name) :
                groups.OrderBy(g => g.Name),
            "displayname" => criteria.SortDescending ?
                groups.OrderByDescending(g => g.DisplayName) :
                groups.OrderBy(g => g.DisplayName),
            "membercount" => criteria.SortDescending ?
                groups.OrderByDescending(g => g.MemberCount) :
                groups.OrderBy(g => g.MemberCount),
            "whencreated" => criteria.SortDescending ?
                groups.OrderByDescending(g => g.WhenCreated) :
                groups.OrderBy(g => g.WhenCreated),
            _ => groups.OrderBy(g => g.Name)
        };

        // Apply paging
        return sortedGroups.Skip(criteria.Skip).Take(criteria.Take).ToList();
    }
}