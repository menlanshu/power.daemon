using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.DirectoryServices.Protocols;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerDaemon.Identity.Configuration;
using PowerDaemon.Identity.Models;
using PowerDaemon.Cache.Services;

namespace PowerDaemon.Identity.Services;

public class ActiveDirectoryService : IActiveDirectoryService, IDisposable
{
    private readonly ILogger<ActiveDirectoryService> _logger;
    private readonly ActiveDirectoryConfiguration _config;
    private readonly ICacheService _cacheService;
    private readonly object _connectionLock = new();
    private PrincipalContext? _principalContext;
    private LdapConnection? _ldapConnection;
    private bool _disposed;

    public ActiveDirectoryService(
        ILogger<ActiveDirectoryService> logger,
        IOptions<ActiveDirectoryConfiguration> config,
        ICacheService cacheService)
    {
        _logger = logger;
        _config = config.Value;
        _cacheService = cacheService;
        InitializeConnections();
    }

    public async Task<AuthenticationResult> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Authenticating user {UserName}", userName);

            // Check cache for failed authentication attempts
            var cacheKey = $"{_config.CacheConfiguration.CacheKeyPrefix}auth_attempts:{userName}";
            var failedAttemptsStr = await _cacheService.GetAsync<string>(cacheKey);
            var failedAttempts = int.TryParse(failedAttemptsStr, out var attempts) ? attempts : 0;
            
            if (failedAttempts >= 5) // Configurable lockout threshold
            {
                _logger.LogWarning("User {UserName} is temporarily locked due to failed authentication attempts", userName);
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorCode = "TooManyFailedAttempts",
                    ErrorDescription = "Account is temporarily locked due to too many failed authentication attempts"
                };
            }

            var isValid = await ValidateCredentialsAsync(userName, password, cancellationToken);
            if (!isValid)
            {
                // Increment failed attempts
                await _cacheService.SetAsync(cacheKey, (failedAttempts + 1).ToString(), TimeSpan.FromMinutes(30));
                
                _logger.LogWarning("Authentication failed for user {UserName}", userName);
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorCode = "InvalidCredentials",
                    ErrorDescription = "Invalid username or password"
                };
            }

            // Clear failed attempts on successful authentication
            await _cacheService.DeleteAsync(cacheKey);

            var user = await GetUserAsync(userName, cancellationToken);
            if (user == null)
            {
                _logger.LogWarning("User {UserName} authenticated but not found in directory", userName);
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorCode = "UserNotFound",
                    ErrorDescription = "User not found in directory"
                };
            }

            if (!user.IsEnabled)
            {
                _logger.LogWarning("User {UserName} is disabled", userName);
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorCode = "AccountDisabled",
                    ErrorDescription = "User account is disabled"
                };
            }

            if (user.IsLocked)
            {
                _logger.LogWarning("User {UserName} account is locked", userName);
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorCode = "AccountLocked",
                    ErrorDescription = "User account is locked"
                };
            }

            if (user.AccountExpires.HasValue && user.AccountExpires.Value < DateTime.UtcNow)
            {
                _logger.LogWarning("User {UserName} account has expired", userName);
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorCode = "AccountExpired",
                    ErrorDescription = "User account has expired"
                };
            }

            // Update user metadata
            user.Metadata.LastAuthenticationAt = DateTime.UtcNow;
            user.Metadata.AuthenticationCount++;

            // Cache successful authentication
            var authCacheKey = $"{_config.CacheConfiguration.CacheKeyPrefix}auth:{userName}";
            await _cacheService.SetAsync(authCacheKey, user, _config.CacheConfiguration.AuthenticationCacheTtl);

            _logger.LogInformation("User {UserName} authenticated successfully", userName);
            return new AuthenticationResult
            {
                Success = true,
                User = user,
                AuthenticationMethod = "ActiveDirectory",
                AuthenticatedAt = DateTime.UtcNow
            };
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

    public async Task<bool> ValidateCredentialsAsync(string userName, string password, CancellationToken cancellationToken = default)
    {
        try
        {
            EnsurePrincipalContext();
            return _principalContext!.ValidateCredentials(userName, password);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating credentials for user {UserName}", userName);
            return false;
        }
    }

    public async Task<User?> GetUserAsync(string userName, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_config.CacheConfiguration.EnableCaching)
            {
                var cacheKey = $"{_config.CacheConfiguration.CacheKeyPrefix}user:{userName}";
                var cachedUser = await _cacheService.GetAsync<User>(cacheKey);
                if (cachedUser != null)
                {
                    _logger.LogDebug("User {UserName} found in cache", userName);
                    return cachedUser;
                }
            }

            _logger.LogDebug("Retrieving user {UserName} from Active Directory", userName);
            EnsurePrincipalContext();

            using var userPrincipal = UserPrincipal.FindByIdentity(_principalContext, IdentityType.SamAccountName, userName);
            if (userPrincipal == null)
            {
                _logger.LogDebug("User {UserName} not found in Active Directory", userName);
                return null;
            }

            var user = await MapUserPrincipalToUser(userPrincipal, cancellationToken);

            if (_config.CacheConfiguration.EnableCaching)
            {
                var cacheKey = $"{_config.CacheConfiguration.CacheKeyPrefix}user:{userName}";
                await _cacheService.SetAsync(cacheKey, user, _config.CacheConfiguration.UserCacheTtl);
            }

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user {UserName}", userName);
            return null;
        }
    }

    public async Task<User?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Retrieving user by email {Email}", email);
            EnsurePrincipalContext();

            using var userPrincipal = UserPrincipal.FindByIdentity(_principalContext, IdentityType.UserPrincipalName, email);
            if (userPrincipal == null)
            {
                _logger.LogDebug("User with email {Email} not found", email);
                return null;
            }

            return await MapUserPrincipalToUser(userPrincipal, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user by email {Email}", email);
            return null;
        }
    }

    public async Task<User?> GetUserByDistinguishedNameAsync(string distinguishedName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Retrieving user by DN {DistinguishedName}", distinguishedName);
            EnsurePrincipalContext();

            using var userPrincipal = UserPrincipal.FindByIdentity(_principalContext, IdentityType.DistinguishedName, distinguishedName);
            if (userPrincipal == null)
            {
                _logger.LogDebug("User with DN {DistinguishedName} not found", distinguishedName);
                return null;
            }

            return await MapUserPrincipalToUser(userPrincipal, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user by DN {DistinguishedName}", distinguishedName);
            return null;
        }
    }

    public async Task<List<User>> SearchUsersAsync(string searchFilter, int maxResults = 100, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Searching users with filter: {SearchFilter}", searchFilter);
            EnsurePrincipalContext();

            var users = new List<User>();
            using var searcher = new UserPrincipal(_principalContext);

            // Apply basic filter
            if (!string.IsNullOrWhiteSpace(searchFilter))
            {
                searcher.Name = $"*{searchFilter}*";
            }

            using var searchResults = new PrincipalSearcher(searcher);
            var results = searchResults.FindAll().Take(maxResults);

            foreach (Principal result in results)
            {
                if (result is UserPrincipal userPrincipal)
                {
                    var user = await MapUserPrincipalToUser(userPrincipal, cancellationToken);
                    users.Add(user);
                }
                result.Dispose();
            }

            _logger.LogDebug("Found {Count} users matching filter", users.Count);
            return users;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching users with filter {SearchFilter}", searchFilter);
            return new List<User>();
        }
    }

    public async Task<List<User>> GetUsersInGroupAsync(string groupName, bool includeNestedGroups = true, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Retrieving users in group {GroupName}", groupName);
            EnsurePrincipalContext();

            var users = new List<User>();
            using var groupPrincipal = GroupPrincipal.FindByIdentity(_principalContext, IdentityType.SamAccountName, groupName);
            
            if (groupPrincipal == null)
            {
                _logger.LogDebug("Group {GroupName} not found", groupName);
                return users;
            }

            var members = includeNestedGroups ? 
                groupPrincipal.GetMembers(true) : 
                groupPrincipal.GetMembers(false);

            foreach (var member in members)
            {
                if (member is UserPrincipal userPrincipal)
                {
                    var user = await MapUserPrincipalToUser(userPrincipal, cancellationToken);
                    users.Add(user);
                }
                member.Dispose();
            }

            _logger.LogDebug("Found {Count} users in group {GroupName}", users.Count, groupName);
            return users;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving users in group {GroupName}", groupName);
            return new List<User>();
        }
    }

    public async Task<Group?> GetGroupAsync(string groupName, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_config.CacheConfiguration.EnableCaching)
            {
                var cacheKey = $"{_config.CacheConfiguration.CacheKeyPrefix}group:{groupName}";
                var cachedGroup = await _cacheService.GetAsync<Group>(cacheKey);
                if (cachedGroup != null)
                {
                    _logger.LogDebug("Group {GroupName} found in cache", groupName);
                    return cachedGroup;
                }
            }

            _logger.LogDebug("Retrieving group {GroupName} from Active Directory", groupName);
            EnsurePrincipalContext();

            using var groupPrincipal = GroupPrincipal.FindByIdentity(_principalContext, IdentityType.SamAccountName, groupName);
            if (groupPrincipal == null)
            {
                _logger.LogDebug("Group {GroupName} not found", groupName);
                return null;
            }

            var group = await MapGroupPrincipalToGroup(groupPrincipal, cancellationToken);

            if (_config.CacheConfiguration.EnableCaching)
            {
                var cacheKey = $"{_config.CacheConfiguration.CacheKeyPrefix}group:{groupName}";
                await _cacheService.SetAsync(cacheKey, group, _config.CacheConfiguration.GroupCacheTtl);
            }

            return group;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving group {GroupName}", groupName);
            return null;
        }
    }

    public async Task<Group?> GetGroupByDistinguishedNameAsync(string distinguishedName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Retrieving group by DN {DistinguishedName}", distinguishedName);
            EnsurePrincipalContext();

            using var groupPrincipal = GroupPrincipal.FindByIdentity(_principalContext, IdentityType.DistinguishedName, distinguishedName);
            if (groupPrincipal == null)
            {
                _logger.LogDebug("Group with DN {DistinguishedName} not found", distinguishedName);
                return null;
            }

            return await MapGroupPrincipalToGroup(groupPrincipal, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving group by DN {DistinguishedName}", distinguishedName);
            return null;
        }
    }

    public async Task<List<Group>> SearchGroupsAsync(string searchFilter, int maxResults = 100, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Searching groups with filter: {SearchFilter}", searchFilter);
            EnsurePrincipalContext();

            var groups = new List<Group>();
            using var searcher = new GroupPrincipal(_principalContext);

            if (!string.IsNullOrWhiteSpace(searchFilter))
            {
                searcher.Name = $"*{searchFilter}*";
            }

            using var searchResults = new PrincipalSearcher(searcher);
            var results = searchResults.FindAll().Take(maxResults);

            foreach (Principal result in results)
            {
                if (result is GroupPrincipal groupPrincipal)
                {
                    var group = await MapGroupPrincipalToGroup(groupPrincipal, cancellationToken);
                    groups.Add(group);
                }
                result.Dispose();
            }

            _logger.LogDebug("Found {Count} groups matching filter", groups.Count);
            return groups;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching groups with filter {SearchFilter}", searchFilter);
            return new List<Group>();
        }
    }

    public async Task<List<Group>> GetUserGroupsAsync(string userName, bool includeNestedGroups = true, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Retrieving groups for user {UserName}", userName);
            EnsurePrincipalContext();

            var groups = new List<Group>();
            using var userPrincipal = UserPrincipal.FindByIdentity(_principalContext, IdentityType.SamAccountName, userName);
            
            if (userPrincipal == null)
            {
                _logger.LogDebug("User {UserName} not found", userName);
                return groups;
            }

            var userGroups = includeNestedGroups ? 
                userPrincipal.GetAuthorizationGroups() : 
                userPrincipal.GetGroups();

            foreach (var groupPrincipal in userGroups.OfType<GroupPrincipal>())
            {
                var group = await MapGroupPrincipalToGroup(groupPrincipal, cancellationToken);
                groups.Add(group);
                groupPrincipal.Dispose();
            }

            _logger.LogDebug("Found {Count} groups for user {UserName}", groups.Count, userName);
            return groups;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving groups for user {UserName}", userName);
            return new List<Group>();
        }
    }

    public async Task<List<Group>> GetNestedGroupsAsync(string groupName, int maxDepth = 10, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Retrieving nested groups for {GroupName} with max depth {MaxDepth}", groupName, maxDepth);
            
            var allGroups = new List<Group>();
            var processedGroups = new HashSet<string>();
            var groupsToProcess = new Queue<(string GroupName, int Depth)>();
            
            groupsToProcess.Enqueue((groupName, 0));

            while (groupsToProcess.Count > 0 && cancellationToken.IsCancellationRequested == false)
            {
                var (currentGroupName, depth) = groupsToProcess.Dequeue();
                
                if (depth >= maxDepth || processedGroups.Contains(currentGroupName))
                    continue;

                processedGroups.Add(currentGroupName);
                
                var group = await GetGroupAsync(currentGroupName, cancellationToken);
                if (group != null)
                {
                    allGroups.Add(group);
                    
                    // Add nested groups to queue
                    foreach (var nestedGroup in group.NestedGroups)
                    {
                        if (!processedGroups.Contains(nestedGroup.Name))
                        {
                            groupsToProcess.Enqueue((nestedGroup.Name, depth + 1));
                        }
                    }
                }
            }

            _logger.LogDebug("Found {Count} nested groups for {GroupName}", allGroups.Count, groupName);
            return allGroups;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving nested groups for {GroupName}", groupName);
            return new List<Group>();
        }
    }

    public async Task<bool> IsUserInGroupAsync(string userName, string groupName, bool includeNestedGroups = true, CancellationToken cancellationToken = default)
    {
        try
        {
            var userGroups = await GetUserGroupsAsync(userName, includeNestedGroups, cancellationToken);
            return userGroups.Any(g => g.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if user {UserName} is in group {GroupName}", userName, groupName);
            return false;
        }
    }

    public async Task<List<User>> GetGroupMembersAsync(string groupName, bool includeNestedGroups = false, CancellationToken cancellationToken = default)
    {
        return await GetUsersInGroupAsync(groupName, includeNestedGroups, cancellationToken);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            EnsurePrincipalContext();
            
            // Try to query a simple object to test connectivity
            using var searcher = new UserPrincipal(_principalContext);
            using var searchResults = new PrincipalSearcher(searcher);
            var result = searchResults.FindOne();
            result?.Dispose();
            
            _logger.LogDebug("Active Directory connection test successful");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Active Directory connection test failed");
            return false;
        }
    }

    public async Task<Dictionary<string, object>> GetConnectionInfoAsync(CancellationToken cancellationToken = default)
    {
        var info = new Dictionary<string, object>
        {
            ["Domain"] = _config.Domain,
            ["LdapServer"] = _config.LdapServer,
            ["LdapPort"] = _config.LdapPort,
            ["UseSsl"] = _config.UseSsl,
            ["UserSearchBase"] = _config.UserSearchBase,
            ["GroupSearchBase"] = _config.GroupSearchBase,
            ["CachingEnabled"] = _config.CacheConfiguration.EnableCaching,
            ["ConnectionEstablished"] = _principalContext != null
        };

        return info;
    }

    public async Task InvalidateUserCacheAsync(string userName)
    {
        if (!_config.CacheConfiguration.EnableCaching) return;
        
        var cacheKey = $"{_config.CacheConfiguration.CacheKeyPrefix}user:{userName}";
        await _cacheService.DeleteAsync(cacheKey);
        
        _logger.LogDebug("Invalidated cache for user {UserName}", userName);
    }

    public async Task InvalidateGroupCacheAsync(string groupName)
    {
        if (!_config.CacheConfiguration.EnableCaching) return;
        
        var cacheKey = $"{_config.CacheConfiguration.CacheKeyPrefix}group:{groupName}";
        await _cacheService.DeleteAsync(cacheKey);
        
        _logger.LogDebug("Invalidated cache for group {GroupName}", groupName);
    }

    public async Task ClearAllCacheAsync()
    {
        if (!_config.CacheConfiguration.EnableCaching) return;
        
        await _cacheService.DeleteByPatternAsync($"{_config.CacheConfiguration.CacheKeyPrefix}*");
        _logger.LogInformation("Cleared all Active Directory cache entries");
    }

    public async Task<List<User>> SynchronizeUsersAsync(List<string> userNames, CancellationToken cancellationToken = default)
    {
        var synchronizedUsers = new List<User>();
        
        foreach (var userName in userNames)
        {
            try
            {
                // Invalidate cache first
                await InvalidateUserCacheAsync(userName);
                
                // Fetch fresh data
                var user = await GetUserAsync(userName, cancellationToken);
                if (user != null)
                {
                    synchronizedUsers.Add(user);
                    _logger.LogDebug("Synchronized user {UserName}", userName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error synchronizing user {UserName}", userName);
            }
        }
        
        _logger.LogInformation("Synchronized {Count} users", synchronizedUsers.Count);
        return synchronizedUsers;
    }

    public async Task<List<Group>> SynchronizeGroupsAsync(List<string> groupNames, CancellationToken cancellationToken = default)
    {
        var synchronizedGroups = new List<Group>();
        
        foreach (var groupName in groupNames)
        {
            try
            {
                // Invalidate cache first
                await InvalidateGroupCacheAsync(groupName);
                
                // Fetch fresh data
                var group = await GetGroupAsync(groupName, cancellationToken);
                if (group != null)
                {
                    synchronizedGroups.Add(group);
                    _logger.LogDebug("Synchronized group {GroupName}", groupName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error synchronizing group {GroupName}", groupName);
            }
        }
        
        _logger.LogInformation("Synchronized {Count} groups", synchronizedGroups.Count);
        return synchronizedGroups;
    }

    private void InitializeConnections()
    {
        try
        {
            _logger.LogDebug("Initializing Active Directory connections");
            
            var contextType = ContextType.Domain;
            var contextOptions = ContextOptions.Negotiate;
            
            if (_config.UseSsl)
            {
                contextOptions |= ContextOptions.SecureSocketLayer;
            }

            _principalContext = new PrincipalContext(
                contextType,
                _config.Domain,
                _config.UserSearchBase,
                contextOptions,
                _config.BindUserName,
                _config.BindPassword);

            _logger.LogInformation("Active Directory connections initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Active Directory connections");
            throw;
        }
    }

    private void EnsurePrincipalContext()
    {
        lock (_connectionLock)
        {
            if (_principalContext == null)
            {
                InitializeConnections();
            }
        }
    }

    private async Task<User> MapUserPrincipalToUser(UserPrincipal userPrincipal, CancellationToken cancellationToken)
    {
        var user = new User
        {
            Id = userPrincipal.Guid?.ToString() ?? userPrincipal.SamAccountName,
            UserName = userPrincipal.SamAccountName ?? string.Empty,
            Email = userPrincipal.EmailAddress ?? string.Empty,
            DisplayName = userPrincipal.DisplayName ?? string.Empty,
            FirstName = userPrincipal.GivenName ?? string.Empty,
            LastName = userPrincipal.Surname ?? string.Empty,
            DistinguishedName = userPrincipal.DistinguishedName ?? string.Empty,
            IsEnabled = userPrincipal.Enabled ?? true,
            IsLocked = userPrincipal.IsAccountLockedOut(),
            LastLogon = userPrincipal.LastLogon,
            PasswordLastSet = userPrincipal.LastPasswordSet,
            AccountExpires = userPrincipal.AccountExpirationDate,
            WhenCreated = userPrincipal.GetUnderlyingObject() is DirectoryEntry de ? (DateTime?)de.Properties["whenCreated"]?.Value : null,
            WhenChanged = userPrincipal.GetUnderlyingObject() is DirectoryEntry de2 ? (DateTime?)de2.Properties["whenChanged"]?.Value : null
        };

        // Map additional properties from DirectoryEntry
        if (userPrincipal.GetUnderlyingObject() is DirectoryEntry directoryEntry)
        {
            user.Department = GetPropertyValue(directoryEntry, "department");
            user.Title = GetPropertyValue(directoryEntry, "title");
            user.PhoneNumber = GetPropertyValue(directoryEntry, "telephoneNumber");
            user.Office = GetPropertyValue(directoryEntry, "physicalDeliveryOfficeName");
            user.Manager = GetPropertyValue(directoryEntry, "manager");

            // Map custom attributes
            foreach (var customAttribute in _config.CustomAttributes)
            {
                var value = GetPropertyValue(directoryEntry, customAttribute.LdapAttribute);
                if (!string.IsNullOrEmpty(value))
                {
                    user.CustomAttributes[customAttribute.Name] = ConvertAttributeValue(value, customAttribute.DataType);
                }
            }
        }

        // Load user groups if enabled
        if (_config.EnableUserGroups)
        {
            try
            {
                var groups = await GetUserGroupsAsync(user.UserName, _config.EnableNestedGroups, cancellationToken);
                user.Groups = groups;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load groups for user {UserName}", user.UserName);
            }
        }

        return user;
    }

    private async Task<Group> MapGroupPrincipalToGroup(GroupPrincipal groupPrincipal, CancellationToken cancellationToken)
    {
        var group = new Group
        {
            Id = groupPrincipal.Guid?.ToString() ?? groupPrincipal.SamAccountName,
            Name = groupPrincipal.SamAccountName ?? string.Empty,
            DisplayName = groupPrincipal.DisplayName ?? string.Empty,
            Description = groupPrincipal.Description ?? string.Empty,
            DistinguishedName = groupPrincipal.DistinguishedName ?? string.Empty,
            IsEnabled = groupPrincipal.GetUnderlyingObject() is DirectoryEntry de && 
                       !de.Properties["userAccountControl"].Value?.ToString()?.Contains("ACCOUNTDISABLE") == true,
            WhenCreated = groupPrincipal.GetUnderlyingObject() is DirectoryEntry de2 ? (DateTime?)de2.Properties["whenCreated"]?.Value : null,
            WhenChanged = groupPrincipal.GetUnderlyingObject() is DirectoryEntry de3 ? (DateTime?)de3.Properties["whenChanged"]?.Value : null
        };

        // Determine group type and scope from DirectoryEntry
        if (groupPrincipal.GetUnderlyingObject() is DirectoryEntry directoryEntry)
        {
            var groupType = directoryEntry.Properties["groupType"]?.Value?.ToString();
            if (int.TryParse(groupType, out var groupTypeInt))
            {
                group.GroupType = (groupTypeInt & 0x80000000) != 0 ? GroupType.Security : GroupType.Distribution;
                
                var scope = groupTypeInt & 0x0000000F;
                group.Scope = scope switch
                {
                    1 => Models.GroupScope.Global,
                    2 => Models.GroupScope.DomainLocal,
                    4 => Models.GroupScope.Universal,
                    _ => Models.GroupScope.Global
                };
            }

            // Map custom attributes
            foreach (var customAttribute in _config.CustomAttributes)
            {
                var value = GetPropertyValue(directoryEntry, customAttribute.LdapAttribute);
                if (!string.IsNullOrEmpty(value))
                {
                    group.CustomAttributes[customAttribute.Name] = ConvertAttributeValue(value, customAttribute.DataType);
                }
            }
        }

        // Get member count
        try
        {
            group.MemberCount = groupPrincipal.GetMembers(false).Count();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get member count for group {GroupName}", group.Name);
        }

        return group;
    }

    private static string GetPropertyValue(DirectoryEntry directoryEntry, string propertyName)
    {
        try
        {
            var property = directoryEntry.Properties[propertyName];
            return property?.Value?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static object ConvertAttributeValue(string value, AttributeDataType dataType)
    {
        try
        {
            return dataType switch
            {
                AttributeDataType.Integer => int.Parse(value),
                AttributeDataType.Boolean => bool.Parse(value),
                AttributeDataType.DateTime => DateTime.Parse(value),
                AttributeDataType.Binary => Convert.FromBase64String(value),
                _ => value
            };
        }
        catch
        {
            return value; // Return original value if conversion fails
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            _principalContext?.Dispose();
            _ldapConnection?.Dispose();
            _logger.LogDebug("Active Directory service disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing Active Directory service");
        }
        finally
        {
            _disposed = true;
        }
    }
}