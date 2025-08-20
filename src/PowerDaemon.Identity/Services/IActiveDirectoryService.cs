using PowerDaemon.Identity.Models;

namespace PowerDaemon.Identity.Services;

public interface IActiveDirectoryService
{
    // Authentication
    Task<AuthenticationResult> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken = default);
    Task<bool> ValidateCredentialsAsync(string userName, string password, CancellationToken cancellationToken = default);

    // User Management
    Task<User?> GetUserAsync(string userName, CancellationToken cancellationToken = default);
    Task<User?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<User?> GetUserByDistinguishedNameAsync(string distinguishedName, CancellationToken cancellationToken = default);
    Task<List<User>> SearchUsersAsync(string searchFilter, int maxResults = 100, CancellationToken cancellationToken = default);
    Task<List<User>> GetUsersInGroupAsync(string groupName, bool includeNestedGroups = true, CancellationToken cancellationToken = default);

    // Group Management
    Task<Group?> GetGroupAsync(string groupName, CancellationToken cancellationToken = default);
    Task<Group?> GetGroupByDistinguishedNameAsync(string distinguishedName, CancellationToken cancellationToken = default);
    Task<List<Group>> SearchGroupsAsync(string searchFilter, int maxResults = 100, CancellationToken cancellationToken = default);
    Task<List<Group>> GetUserGroupsAsync(string userName, bool includeNestedGroups = true, CancellationToken cancellationToken = default);
    Task<List<Group>> GetNestedGroupsAsync(string groupName, int maxDepth = 10, CancellationToken cancellationToken = default);

    // Group Membership
    Task<bool> IsUserInGroupAsync(string userName, string groupName, bool includeNestedGroups = true, CancellationToken cancellationToken = default);
    Task<List<User>> GetGroupMembersAsync(string groupName, bool includeNestedGroups = false, CancellationToken cancellationToken = default);

    // Connection Management
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
    Task<Dictionary<string, object>> GetConnectionInfoAsync(CancellationToken cancellationToken = default);

    // Caching
    Task InvalidateUserCacheAsync(string userName);
    Task InvalidateGroupCacheAsync(string groupName);
    Task ClearAllCacheAsync();

    // Synchronization
    Task<List<User>> SynchronizeUsersAsync(List<string> userNames, CancellationToken cancellationToken = default);
    Task<List<Group>> SynchronizeGroupsAsync(List<string> groupNames, CancellationToken cancellationToken = default);
}

public interface IUserService
{
    // User Operations
    Task<User?> GetUserAsync(string identifier, CancellationToken cancellationToken = default);
    Task<List<User>> SearchUsersAsync(UserSearchCriteria criteria, CancellationToken cancellationToken = default);
    Task<User> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default);
    Task<User> UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteUserAsync(string userId, CancellationToken cancellationToken = default);

    // User Authentication
    Task<AuthenticationResult> AuthenticateUserAsync(string userName, string password, AuthenticationOptions? options = null, CancellationToken cancellationToken = default);
    Task<PowerDaemonTokenValidationResult> ValidateTokenAsync(string token, CancellationToken cancellationToken = default);
    Task<AuthenticationResult> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task<bool> RevokeTokenAsync(string token, CancellationToken cancellationToken = default);

    // User Authorization
    Task<bool> HasPermissionAsync(string userId, string resource, string action, Dictionary<string, object>? context = null, CancellationToken cancellationToken = default);
    Task<List<Permission>> GetUserPermissionsAsync(string userId, string? resource = null, CancellationToken cancellationToken = default);
    Task<List<Role>> GetUserRolesAsync(string userId, CancellationToken cancellationToken = default);

    // User Sessions
    Task<string> CreateSessionAsync(string userId, SessionOptions? options = null, CancellationToken cancellationToken = default);
    Task<bool> ValidateSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<bool> RevokeSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<List<string>> GetActiveSessionsAsync(string userId, CancellationToken cancellationToken = default);

    // User Status
    Task<bool> IsUserEnabledAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> IsUserLockedAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> EnableUserAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> DisableUserAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> UnlockUserAsync(string userId, CancellationToken cancellationToken = default);
}

public interface IGroupService
{
    // Group Operations
    Task<Group?> GetGroupAsync(string identifier, CancellationToken cancellationToken = default);
    Task<List<Group>> SearchGroupsAsync(GroupSearchCriteria criteria, CancellationToken cancellationToken = default);
    Task<Group> CreateGroupAsync(CreateGroupRequest request, CancellationToken cancellationToken = default);
    Task<Group> UpdateGroupAsync(UpdateGroupRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteGroupAsync(string groupId, CancellationToken cancellationToken = default);

    // Group Membership
    Task<List<User>> GetGroupMembersAsync(string groupId, bool includeNestedGroups = false, CancellationToken cancellationToken = default);
    Task<bool> AddUserToGroupAsync(string groupId, string userId, CancellationToken cancellationToken = default);
    Task<bool> RemoveUserFromGroupAsync(string groupId, string userId, CancellationToken cancellationToken = default);
    Task<bool> IsUserInGroupAsync(string groupId, string userId, bool includeNestedGroups = true, CancellationToken cancellationToken = default);

    // Nested Groups
    Task<List<Group>> GetNestedGroupsAsync(string groupId, int maxDepth = 10, CancellationToken cancellationToken = default);
    Task<bool> AddNestedGroupAsync(string parentGroupId, string childGroupId, CancellationToken cancellationToken = default);
    Task<bool> RemoveNestedGroupAsync(string parentGroupId, string childGroupId, CancellationToken cancellationToken = default);

    // Group Roles and Permissions
    Task<List<Role>> GetGroupRolesAsync(string groupId, CancellationToken cancellationToken = default);
    Task<bool> AssignRoleToGroupAsync(string groupId, string roleId, CancellationToken cancellationToken = default);
    Task<bool> RemoveRoleFromGroupAsync(string groupId, string roleId, CancellationToken cancellationToken = default);
}

public interface IRoleService
{
    // Role Operations
    Task<Role?> GetRoleAsync(string roleId, CancellationToken cancellationToken = default);
    Task<List<Role>> GetAllRolesAsync(CancellationToken cancellationToken = default);
    Task<List<Role>> SearchRolesAsync(RoleSearchCriteria criteria, CancellationToken cancellationToken = default);
    Task<Role> CreateRoleAsync(CreateRoleRequest request, CancellationToken cancellationToken = default);
    Task<Role> UpdateRoleAsync(UpdateRoleRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteRoleAsync(string roleId, CancellationToken cancellationToken = default);

    // Role Permissions
    Task<List<Permission>> GetRolePermissionsAsync(string roleId, CancellationToken cancellationToken = default);
    Task<bool> AssignPermissionToRoleAsync(string roleId, string permissionId, CancellationToken cancellationToken = default);
    Task<bool> RemovePermissionFromRoleAsync(string roleId, string permissionId, CancellationToken cancellationToken = default);

    // Role Assignment
    Task<List<User>> GetUsersWithRoleAsync(string roleId, CancellationToken cancellationToken = default);
    Task<List<Group>> GetGroupsWithRoleAsync(string roleId, CancellationToken cancellationToken = default);
    Task<bool> AssignRoleToUserAsync(string roleId, string userId, CancellationToken cancellationToken = default);
    Task<bool> RemoveRoleFromUserAsync(string roleId, string userId, CancellationToken cancellationToken = default);
}

public interface IPermissionService
{
    // Permission Operations
    Task<Permission?> GetPermissionAsync(string permissionId, CancellationToken cancellationToken = default);
    Task<List<Permission>> GetAllPermissionsAsync(CancellationToken cancellationToken = default);
    Task<List<Permission>> SearchPermissionsAsync(PermissionSearchCriteria criteria, CancellationToken cancellationToken = default);
    Task<Permission> CreatePermissionAsync(CreatePermissionRequest request, CancellationToken cancellationToken = default);
    Task<Permission> UpdatePermissionAsync(UpdatePermissionRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeletePermissionAsync(string permissionId, CancellationToken cancellationToken = default);

    // Permission Evaluation
    Task<bool> EvaluatePermissionAsync(string userId, string resource, string action, Dictionary<string, object>? context = null, CancellationToken cancellationToken = default);
    Task<List<Permission>> GetEffectivePermissionsAsync(string userId, string? resource = null, CancellationToken cancellationToken = default);
    Task<PermissionEvaluationResult> EvaluatePermissionsAsync(PermissionEvaluationRequest request, CancellationToken cancellationToken = default);

    // Permission Hierarchy
    Task<List<Permission>> GetChildPermissionsAsync(string permissionId, CancellationToken cancellationToken = default);
    Task<List<Permission>> GetParentPermissionsAsync(string permissionId, CancellationToken cancellationToken = default);
}

// Supporting Models
public class UserSearchCriteria
{
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public string? Department { get; set; }
    public string? Title { get; set; }
    public bool? IsEnabled { get; set; }
    public bool? IsLocked { get; set; }
    public List<string>? Groups { get; set; }
    public List<string>? Roles { get; set; }
    public int Skip { get; set; } = 0;
    public int Take { get; set; } = 100;
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; } = false;
}

public class GroupSearchCriteria
{
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public GroupType? GroupType { get; set; }
    public GroupScope? Scope { get; set; }
    public bool? IsEnabled { get; set; }
    public int Skip { get; set; } = 0;
    public int Take { get; set; } = 100;
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; } = false;
}

public class RoleSearchCriteria
{
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public RoleType? RoleType { get; set; }
    public bool? IsBuiltIn { get; set; }
    public bool? IsEnabled { get; set; }
    public int Skip { get; set; } = 0;
    public int Take { get; set; } = 100;
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; } = false;
}

public class PermissionSearchCriteria
{
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Resource { get; set; }
    public string? Action { get; set; }
    public PermissionType? PermissionType { get; set; }
    public PermissionScope? Scope { get; set; }
    public bool? IsBuiltIn { get; set; }
    public bool? IsEnabled { get; set; }
    public int Skip { get; set; } = 0;
    public int Take { get; set; } = 100;
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; } = false;
}

public class CreateUserRequest
{
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? Title { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Office { get; set; }
    public List<string> GroupIds { get; set; } = new();
    public List<string> RoleIds { get; set; } = new();
    public Dictionary<string, object> CustomAttributes { get; set; } = new();
    public bool IsEnabled { get; set; } = true;
}

public class UpdateUserRequest
{
    public string UserId { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Department { get; set; }
    public string? Title { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Office { get; set; }
    public List<string>? GroupIds { get; set; }
    public List<string>? RoleIds { get; set; }
    public Dictionary<string, object>? CustomAttributes { get; set; }
    public bool? IsEnabled { get; set; }
}

public class CreateGroupRequest
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public GroupType GroupType { get; set; } = GroupType.Security;
    public GroupScope Scope { get; set; } = GroupScope.Global;
    public List<string> MemberIds { get; set; } = new();
    public List<string> RoleIds { get; set; } = new();
    public Dictionary<string, object> CustomAttributes { get; set; } = new();
    public bool IsEnabled { get; set; } = true;
}

public class UpdateGroupRequest
{
    public string GroupId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public List<string>? MemberIds { get; set; }
    public List<string>? RoleIds { get; set; }
    public Dictionary<string, object>? CustomAttributes { get; set; }
    public bool? IsEnabled { get; set; }
}

public class CreateRoleRequest
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public RoleType RoleType { get; set; } = RoleType.Custom;
    public List<string> PermissionIds { get; set; } = new();
    public int Priority { get; set; } = 0;
    public bool IsEnabled { get; set; } = true;
}

public class UpdateRoleRequest
{
    public string RoleId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public List<string>? PermissionIds { get; set; }
    public int? Priority { get; set; }
    public bool? IsEnabled { get; set; }
}

public class CreatePermissionRequest
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Resource { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public PermissionType PermissionType { get; set; } = PermissionType.Allow;
    public PermissionScope Scope { get; set; } = PermissionScope.Global;
    public List<PermissionCondition> Conditions { get; set; } = new();
    public int Priority { get; set; } = 0;
    public bool IsEnabled { get; set; } = true;
}

public class UpdatePermissionRequest
{
    public string PermissionId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Resource { get; set; }
    public string? Action { get; set; }
    public PermissionType? PermissionType { get; set; }
    public PermissionScope? Scope { get; set; }
    public List<PermissionCondition>? Conditions { get; set; }
    public int? Priority { get; set; }
    public bool? IsEnabled { get; set; }
}

public class AuthenticationOptions
{
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool RememberMe { get; set; } = false;
    public TimeSpan? TokenExpiration { get; set; }
    public List<string>? RequestedScopes { get; set; }
    public Dictionary<string, object> CustomClaims { get; set; } = new();
}

public class SessionOptions
{
    public TimeSpan? Duration { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool AllowConcurrentSessions { get; set; } = true;
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class PermissionEvaluationRequest
{
    public string UserId { get; set; } = string.Empty;
    public List<PermissionCheck> Checks { get; set; } = new();
    public Dictionary<string, object> Context { get; set; } = new();
}

public class PermissionCheck
{
    public string Resource { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public Dictionary<string, object> Context { get; set; } = new();
}

public class PermissionEvaluationResult
{
    public string UserId { get; set; } = string.Empty;
    public List<PermissionCheckResult> Results { get; set; } = new();
    public bool AllGranted { get; set; }
    public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;
}

public class PermissionCheckResult
{
    public string Resource { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public bool IsGranted { get; set; }
    public string? DenyReason { get; set; }
    public List<Permission> GrantingPermissions { get; set; } = new();
    public List<Permission> DenyingPermissions { get; set; } = new();
}