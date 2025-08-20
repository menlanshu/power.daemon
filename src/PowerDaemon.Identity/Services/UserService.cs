using Microsoft.Extensions.Logging;
using PowerDaemon.Identity.Models;
using PowerDaemon.Cache.Services;

namespace PowerDaemon.Identity.Services;

public class UserService : IUserService
{
    private readonly ILogger<UserService> _logger;
    private readonly IActiveDirectoryService _activeDirectoryService;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ICacheService _cacheService;
    private readonly IRoleService _roleService;
    private readonly IPermissionService _permissionService;

    public UserService(
        ILogger<UserService> logger,
        IActiveDirectoryService activeDirectoryService,
        IJwtTokenService jwtTokenService,
        ICacheService cacheService,
        IRoleService roleService,
        IPermissionService permissionService)
    {
        _logger = logger;
        _activeDirectoryService = activeDirectoryService;
        _jwtTokenService = jwtTokenService;
        _cacheService = cacheService;
        _roleService = roleService;
        _permissionService = permissionService;
    }

    public async Task<User?> GetUserAsync(string identifier, CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to get user by username first
            var user = await _activeDirectoryService.GetUserAsync(identifier, cancellationToken);
            
            // If not found, try by email
            if (user == null)
            {
                user = await _activeDirectoryService.GetUserByEmailAsync(identifier, cancellationToken);
            }

            if (user != null)
            {
                // Enrich with roles and permissions
                user = await EnrichUserWithRolesAndPermissions(user, cancellationToken);
            }

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user {Identifier}", identifier);
            return null;
        }
    }

    public async Task<List<User>> SearchUsersAsync(UserSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Searching users with criteria");

            var users = new List<User>();

            // If specific groups or roles are specified, get users from those
            if (criteria.Groups?.Any() == true)
            {
                foreach (var groupName in criteria.Groups)
                {
                    var groupUsers = await _activeDirectoryService.GetUsersInGroupAsync(groupName, true, cancellationToken);
                    users.AddRange(groupUsers);
                }
            }
            else
            {
                // Perform general search
                var searchFilter = BuildSearchFilter(criteria);
                var adUsers = await _activeDirectoryService.SearchUsersAsync(searchFilter, criteria.Take, cancellationToken);
                users.AddRange(adUsers);
            }

            // Apply additional filtering
            users = ApplyUserFilters(users, criteria);

            // Enrich with roles and permissions
            var enrichedUsers = new List<User>();
            foreach (var user in users)
            {
                var enrichedUser = await EnrichUserWithRolesAndPermissions(user, cancellationToken);
                enrichedUsers.Add(enrichedUser);
            }

            // Apply role-based filtering
            if (criteria.Roles?.Any() == true)
            {
                enrichedUsers = enrichedUsers.Where(u => 
                    u.Roles.Any(r => criteria.Roles.Contains(r.Name, StringComparer.OrdinalIgnoreCase))
                ).ToList();
            }

            // Apply sorting and paging
            enrichedUsers = ApplySortingAndPaging(enrichedUsers, criteria);

            _logger.LogDebug("Found {Count} users matching criteria", enrichedUsers.Count);
            return enrichedUsers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching users");
            return new List<User>();
        }
    }

    public async Task<User> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        // Note: Creating users in AD requires different approach and permissions
        // This is a placeholder for custom user creation logic
        throw new NotImplementedException("User creation in Active Directory requires administrative access and is not implemented in this service");
    }

    public async Task<User> UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        // Note: Updating users in AD requires different approach and permissions
        // This is a placeholder for custom user update logic
        throw new NotImplementedException("User updates in Active Directory require administrative access and is not implemented in this service");
    }

    public async Task<bool> DeleteUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        // Note: Deleting users in AD requires different approach and permissions
        throw new NotImplementedException("User deletion in Active Directory requires administrative access and is not implemented in this service");
    }

    public async Task<AuthenticationResult> AuthenticateUserAsync(string userName, string password, AuthenticationOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Authenticating user {UserName}", userName);

            var authResult = await _activeDirectoryService.AuthenticateAsync(userName, password, cancellationToken);
            if (!authResult.Success || authResult.User == null)
            {
                return authResult;
            }

            // Enrich user with roles and permissions
            authResult.User = await EnrichUserWithRolesAndPermissions(authResult.User, cancellationToken);

            // Generate JWT tokens
            var accessToken = await _jwtTokenService.GenerateAccessTokenAsync(authResult.User, options, cancellationToken);
            var refreshToken = await _jwtTokenService.GenerateRefreshTokenAsync(authResult.User, cancellationToken);

            // Create session
            var sessionId = await CreateSessionAsync(authResult.User.Id, new SessionOptions
            {
                IpAddress = options?.IpAddress,
                UserAgent = options?.UserAgent,
                AllowConcurrentSessions = true
            }, cancellationToken);

            authResult.AccessToken = accessToken;
            authResult.RefreshToken = refreshToken;
            authResult.TokenType = "Bearer";
            authResult.ExpiresIn = (int)TimeSpan.FromHours(1).TotalSeconds; // Default from JWT config
            authResult.SessionId = sessionId;
            authResult.IpAddress = options?.IpAddress;
            authResult.UserAgent = options?.UserAgent;

            _logger.LogInformation("User {UserName} authenticated successfully", userName);
            return authResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error authenticating user {UserName}", userName);
            return new AuthenticationResult
            {
                Success = false,
                ErrorCode = "AuthenticationError",
                ErrorDescription = "An error occurred during authentication"
            };
        }
    }

    public async Task<PowerDaemonTokenValidationResult> ValidateTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        return await _jwtTokenService.ValidateTokenAsync(token, cancellationToken);
    }

    public async Task<AuthenticationResult> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        return await _jwtTokenService.RefreshTokenAsync(refreshToken, cancellationToken);
    }

    public async Task<bool> RevokeTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        return await _jwtTokenService.RevokeTokenAsync(token, cancellationToken);
    }

    public async Task<bool> HasPermissionAsync(string userId, string resource, string action, Dictionary<string, object>? context = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _permissionService.EvaluatePermissionAsync(userId, resource, action, context, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking permission for user {UserId}", userId);
            return false;
        }
    }

    public async Task<List<Permission>> GetUserPermissionsAsync(string userId, string? resource = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _permissionService.GetEffectivePermissionsAsync(userId, resource, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting permissions for user {UserId}", userId);
            return new List<Permission>();
        }
    }

    public async Task<List<Role>> GetUserRolesAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await GetUserAsync(userId, cancellationToken);
            return user?.Roles ?? new List<Role>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting roles for user {UserId}", userId);
            return new List<Role>();
        }
    }

    public async Task<string> CreateSessionAsync(string userId, SessionOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var sessionId = Guid.NewGuid().ToString();
            var session = new UserSession
            {
                SessionId = sessionId,
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(options?.Duration ?? TimeSpan.FromHours(8)),
                IpAddress = options?.IpAddress,
                UserAgent = options?.UserAgent,
                IsActive = true,
                Metadata = options?.Metadata ?? new Dictionary<string, object>()
            };

            var cacheKey = $"session:{sessionId}";
            var ttl = session.ExpiresAt - DateTime.UtcNow;
            await _cacheService.SetAsync(cacheKey, session, ttl);

            // Add to user's active sessions
            var userSessionsKey = $"user_sessions:{userId}";
            var userSessions = await _cacheService.GetAsync<List<string>>(userSessionsKey) ?? new List<string>();
            
            if (!options?.AllowConcurrentSessions == true)
            {
                // Revoke existing sessions
                foreach (var existingSessionId in userSessions)
                {
                    await RevokeSessionAsync(existingSessionId, cancellationToken);
                }
                userSessions.Clear();
            }

            userSessions.Add(sessionId);
            await _cacheService.SetAsync(userSessionsKey, userSessions, TimeSpan.FromDays(1));

            _logger.LogDebug("Session created for user {UserId}: {SessionId}", userId, sessionId);
            return sessionId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating session for user {UserId}", userId);
            throw;
        }
    }

    public async Task<bool> ValidateSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var cacheKey = $"session:{sessionId}";
            var session = await _cacheService.GetAsync<UserSession>(cacheKey);
            
            if (session == null || !session.IsActive || session.ExpiresAt < DateTime.UtcNow)
            {
                return false;
            }

            // Update last accessed time
            session.LastAccessedAt = DateTime.UtcNow;
            var ttl = session.ExpiresAt - DateTime.UtcNow;
            await _cacheService.SetAsync(cacheKey, session, ttl);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating session {SessionId}", sessionId);
            return false;
        }
    }

    public async Task<bool> RevokeSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var cacheKey = $"session:{sessionId}";
            var session = await _cacheService.GetAsync<UserSession>(cacheKey);
            
            if (session != null)
            {
                session.IsActive = false;
                session.RevokedAt = DateTime.UtcNow;
                
                var ttl = session.ExpiresAt - DateTime.UtcNow;
                if (ttl > TimeSpan.Zero)
                {
                    await _cacheService.SetAsync(cacheKey, session, ttl);
                }

                // Remove from user's active sessions
                var userSessionsKey = $"user_sessions:{session.UserId}";
                var userSessions = await _cacheService.GetAsync<List<string>>(userSessionsKey) ?? new List<string>();
                userSessions.Remove(sessionId);
                await _cacheService.SetAsync(userSessionsKey, userSessions, TimeSpan.FromDays(1));

                _logger.LogDebug("Session revoked: {SessionId}", sessionId);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking session {SessionId}", sessionId);
            return false;
        }
    }

    public async Task<List<string>> GetActiveSessionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var userSessionsKey = $"user_sessions:{userId}";
            var sessionIds = await _cacheService.GetAsync<List<string>>(userSessionsKey) ?? new List<string>();
            
            var activeSessions = new List<string>();
            
            foreach (var sessionId in sessionIds)
            {
                if (await ValidateSessionAsync(sessionId, cancellationToken))
                {
                    activeSessions.Add(sessionId);
                }
            }

            return activeSessions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active sessions for user {UserId}", userId);
            return new List<string>();
        }
    }

    public async Task<bool> IsUserEnabledAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await GetUserAsync(userId, cancellationToken);
        return user?.IsEnabled ?? false;
    }

    public async Task<bool> IsUserLockedAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await GetUserAsync(userId, cancellationToken);
        return user?.IsLocked ?? false;
    }

    public async Task<bool> EnableUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        // This would require AD administrative permissions
        throw new NotImplementedException("User enable/disable operations require Active Directory administrative access");
    }

    public async Task<bool> DisableUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        // This would require AD administrative permissions
        throw new NotImplementedException("User enable/disable operations require Active Directory administrative access");
    }

    public async Task<bool> UnlockUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        // This would require AD administrative permissions
        throw new NotImplementedException("User unlock operations require Active Directory administrative access");
    }

    private async Task<User> EnrichUserWithRolesAndPermissions(User user, CancellationToken cancellationToken)
    {
        try
        {
            // Map AD groups to roles
            var roles = await MapGroupsToRoles(user.Groups, cancellationToken);
            user.Roles = roles;

            // Get effective permissions
            var permissions = await _permissionService.GetEffectivePermissionsAsync(user.Id, null, cancellationToken);
            user.Permissions = permissions;

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error enriching user {UserId} with roles and permissions", user.Id);
            return user;
        }
    }

    private async Task<List<Role>> MapGroupsToRoles(List<Group> groups, CancellationToken cancellationToken)
    {
        var roles = new List<Role>();

        foreach (var group in groups)
        {
            var groupRoles = await _roleService.GetGroupsWithRoleAsync(group.Id, cancellationToken);
            foreach (var role in group.Roles)
            {
                if (!roles.Any(r => r.Id == role.Id))
                {
                    roles.Add(role);
                }
            }
        }

        // Add default roles based on group membership patterns
        var defaultRoles = await GetDefaultRolesForGroups(groups, cancellationToken);
        foreach (var role in defaultRoles)
        {
            if (!roles.Any(r => r.Id == role.Id))
            {
                roles.Add(role);
            }
        }

        return roles;
    }

    private async Task<List<Role>> GetDefaultRolesForGroups(List<Group> groups, CancellationToken cancellationToken)
    {
        var roles = new List<Role>();

        // Default role mapping based on common AD group patterns
        var groupNameRoleMap = new Dictionary<string, string[]>
        {
            ["Domain Admins"] = new[] { "Administrator" },
            ["PowerDaemon Admins"] = new[] { "Administrator" },
            ["PowerDaemon Operators"] = new[] { "Operator" },
            ["PowerDaemon Viewers"] = new[] { "Viewer" },
            ["Deployment Managers"] = new[] { "DeploymentManager" },
            ["Service Managers"] = new[] { "ServiceManager" },
            ["Server Managers"] = new[] { "ServerManager" },
            ["Monitoring Users"] = new[] { "MonitoringUser" },
            ["Auditors"] = new[] { "Auditor" }
        };

        foreach (var group in groups)
        {
            foreach (var kvp in groupNameRoleMap)
            {
                if (group.Name.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var roleName in kvp.Value)
                    {
                        var role = await _roleService.GetAllRolesAsync(cancellationToken);
                        var matchingRole = role.FirstOrDefault(r => r.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase));
                        if (matchingRole != null && !roles.Any(r => r.Id == matchingRole.Id))
                        {
                            roles.Add(matchingRole);
                        }
                    }
                }
            }
        }

        return roles;
    }

    private static string BuildSearchFilter(UserSearchCriteria criteria)
    {
        var filters = new List<string>();

        if (!string.IsNullOrWhiteSpace(criteria.UserName))
            filters.Add(criteria.UserName);

        if (!string.IsNullOrWhiteSpace(criteria.DisplayName))
            filters.Add(criteria.DisplayName);

        if (!string.IsNullOrWhiteSpace(criteria.Email))
            filters.Add(criteria.Email);

        if (!string.IsNullOrWhiteSpace(criteria.Department))
            filters.Add(criteria.Department);

        return string.Join(" ", filters);
    }

    private static List<User> ApplyUserFilters(List<User> users, UserSearchCriteria criteria)
    {
        var filteredUsers = users.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(criteria.UserName))
        {
            filteredUsers = filteredUsers.Where(u => 
                u.UserName.Contains(criteria.UserName, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(criteria.Email))
        {
            filteredUsers = filteredUsers.Where(u => 
                u.Email.Contains(criteria.Email, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(criteria.DisplayName))
        {
            filteredUsers = filteredUsers.Where(u => 
                u.DisplayName.Contains(criteria.DisplayName, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(criteria.Department))
        {
            filteredUsers = filteredUsers.Where(u => 
                u.Department.Contains(criteria.Department, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(criteria.Title))
        {
            filteredUsers = filteredUsers.Where(u => 
                u.Title.Contains(criteria.Title, StringComparison.OrdinalIgnoreCase));
        }

        if (criteria.IsEnabled.HasValue)
        {
            filteredUsers = filteredUsers.Where(u => u.IsEnabled == criteria.IsEnabled.Value);
        }

        if (criteria.IsLocked.HasValue)
        {
            filteredUsers = filteredUsers.Where(u => u.IsLocked == criteria.IsLocked.Value);
        }

        return filteredUsers.ToList();
    }

    private static List<User> ApplySortingAndPaging(List<User> users, UserSearchCriteria criteria)
    {
        // Apply sorting
        var sortedUsers = criteria.SortBy?.ToLower() switch
        {
            "username" => criteria.SortDescending ? 
                users.OrderByDescending(u => u.UserName) : 
                users.OrderBy(u => u.UserName),
            "displayname" => criteria.SortDescending ? 
                users.OrderByDescending(u => u.DisplayName) : 
                users.OrderBy(u => u.DisplayName),
            "email" => criteria.SortDescending ? 
                users.OrderByDescending(u => u.Email) : 
                users.OrderBy(u => u.Email),
            "department" => criteria.SortDescending ? 
                users.OrderByDescending(u => u.Department) : 
                users.OrderBy(u => u.Department),
            "lastlogon" => criteria.SortDescending ? 
                users.OrderByDescending(u => u.LastLogon) : 
                users.OrderBy(u => u.LastLogon),
            _ => users.OrderBy(u => u.DisplayName)
        };

        // Apply paging
        return sortedUsers.Skip(criteria.Skip).Take(criteria.Take).ToList();
    }
}

public class UserSession
{
    public string SessionId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? LastAccessedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool IsActive { get; set; } = true;
    public Dictionary<string, object> Metadata { get; set; } = new();
}