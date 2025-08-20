using System.Text.Json.Serialization;

namespace PowerDaemon.Identity.Models;

public class User
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("userName")]
    public string UserName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("firstName")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("lastName")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("department")]
    public string Department { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("phoneNumber")]
    public string PhoneNumber { get; set; } = string.Empty;

    [JsonPropertyName("office")]
    public string Office { get; set; } = string.Empty;

    [JsonPropertyName("manager")]
    public string Manager { get; set; } = string.Empty;

    [JsonPropertyName("distinguishedName")]
    public string DistinguishedName { get; set; } = string.Empty;

    [JsonPropertyName("groups")]
    public List<Group> Groups { get; set; } = new();

    [JsonPropertyName("roles")]
    public List<Role> Roles { get; set; } = new();

    [JsonPropertyName("permissions")]
    public List<Permission> Permissions { get; set; } = new();

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;

    [JsonPropertyName("isLocked")]
    public bool IsLocked { get; set; } = false;

    [JsonPropertyName("passwordExpired")]
    public bool PasswordExpired { get; set; } = false;

    [JsonPropertyName("lastLogon")]
    public DateTime? LastLogon { get; set; }

    [JsonPropertyName("passwordLastSet")]
    public DateTime? PasswordLastSet { get; set; }

    [JsonPropertyName("accountExpires")]
    public DateTime? AccountExpires { get; set; }

    [JsonPropertyName("whenCreated")]
    public DateTime? WhenCreated { get; set; }

    [JsonPropertyName("whenChanged")]
    public DateTime? WhenChanged { get; set; }

    [JsonPropertyName("customAttributes")]
    public Dictionary<string, object> CustomAttributes { get; set; } = new();

    [JsonPropertyName("metadata")]
    public UserMetadata Metadata { get; set; } = new();
}

public class Group
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("distinguishedName")]
    public string DistinguishedName { get; set; } = string.Empty;

    [JsonPropertyName("groupType")]
    public GroupType GroupType { get; set; } = GroupType.Security;

    [JsonPropertyName("scope")]
    public GroupScope Scope { get; set; } = GroupScope.Global;

    [JsonPropertyName("memberCount")]
    public int MemberCount { get; set; } = 0;

    [JsonPropertyName("nestedGroups")]
    public List<Group> NestedGroups { get; set; } = new();

    [JsonPropertyName("roles")]
    public List<Role> Roles { get; set; } = new();

    [JsonPropertyName("permissions")]
    public List<Permission> Permissions { get; set; } = new();

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;

    [JsonPropertyName("whenCreated")]
    public DateTime? WhenCreated { get; set; }

    [JsonPropertyName("whenChanged")]
    public DateTime? WhenChanged { get; set; }

    [JsonPropertyName("customAttributes")]
    public Dictionary<string, object> CustomAttributes { get; set; } = new();
}

public class Role
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("permissions")]
    public List<Permission> Permissions { get; set; } = new();

    [JsonPropertyName("roleType")]
    public RoleType RoleType { get; set; } = RoleType.Custom;

    [JsonPropertyName("isBuiltIn")]
    public bool IsBuiltIn { get; set; } = false;

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 0;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("createdBy")]
    public string CreatedBy { get; set; } = string.Empty;

    [JsonPropertyName("updatedBy")]
    public string UpdatedBy { get; set; } = string.Empty;
}

public class Permission
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("resource")]
    public string Resource { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("permissionType")]
    public PermissionType PermissionType { get; set; } = PermissionType.Allow;

    [JsonPropertyName("scope")]
    public PermissionScope Scope { get; set; } = PermissionScope.Global;

    [JsonPropertyName("conditions")]
    public List<PermissionCondition> Conditions { get; set; } = new();

    [JsonPropertyName("isBuiltIn")]
    public bool IsBuiltIn { get; set; } = false;

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 0;
}

public class PermissionCondition
{
    [JsonPropertyName("attribute")]
    public string Attribute { get; set; } = string.Empty;

    [JsonPropertyName("operator")]
    public ConditionOperator Operator { get; set; } = ConditionOperator.Equals;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("dataType")]
    public ConditionDataType DataType { get; set; } = ConditionDataType.String;
}

public class UserMetadata
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = "ActiveDirectory";

    [JsonPropertyName("lastSyncAt")]
    public DateTime? LastSyncAt { get; set; }

    [JsonPropertyName("lastAuthenticationAt")]
    public DateTime? LastAuthenticationAt { get; set; }

    [JsonPropertyName("authenticationCount")]
    public int AuthenticationCount { get; set; } = 0;

    [JsonPropertyName("failedAuthenticationCount")]
    public int FailedAuthenticationCount { get; set; } = 0;

    [JsonPropertyName("lastFailedAuthenticationAt")]
    public DateTime? LastFailedAuthenticationAt { get; set; }

    [JsonPropertyName("lockoutEnd")]
    public DateTime? LockoutEnd { get; set; }

    [JsonPropertyName("sessionIds")]
    public List<string> SessionIds { get; set; } = new();

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("customMetadata")]
    public Dictionary<string, object> CustomMetadata { get; set; } = new();
}

public class AuthenticationResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("user")]
    public User? User { get; set; }

    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("tokenType")]
    public string TokenType { get; set; } = "Bearer";

    [JsonPropertyName("expiresIn")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; set; }

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = string.Empty;

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("errorDescription")]
    public string? ErrorDescription { get; set; }

    [JsonPropertyName("authenticationMethod")]
    public string AuthenticationMethod { get; set; } = "ActiveDirectory";

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("authenticatedAt")]
    public DateTime AuthenticatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("ipAddress")]
    public string? IpAddress { get; set; }

    [JsonPropertyName("userAgent")]
    public string? UserAgent { get; set; }
}

public class PowerDaemonTokenValidationResult
{
    [JsonPropertyName("isValid")]
    public bool IsValid { get; set; }

    [JsonPropertyName("user")]
    public User? User { get; set; }

    [JsonPropertyName("claims")]
    public Dictionary<string, object> Claims { get; set; } = new();

    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; set; }

    [JsonPropertyName("isExpired")]
    public bool IsExpired { get; set; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("errorDescription")]
    public string? ErrorDescription { get; set; }
}

// Enums
public enum GroupType
{
    Security,
    Distribution
}

public enum GroupScope
{
    DomainLocal,
    Global,
    Universal
}

public enum RoleType
{
    System,
    Application,
    Custom
}

public enum PermissionType
{
    Allow,
    Deny
}

public enum PermissionScope
{
    Global,
    Resource,
    Context
}

public enum ConditionOperator
{
    Equals,
    NotEquals,
    Contains,
    NotContains,
    StartsWith,
    EndsWith,
    GreaterThan,
    LessThan,
    GreaterThanOrEqual,
    LessThanOrEqual,
    In,
    NotIn,
    Regex
}

public enum ConditionDataType
{
    String,
    Integer,
    Boolean,
    DateTime,
    Array
}

// Permission evaluation request and result models
public class PermissionEvaluationRequest
{
    public string UserId { get; set; } = string.Empty;
    public List<PermissionCheck> PermissionChecks { get; set; } = new();
    public Dictionary<string, object>? Context { get; set; }
}

public class PermissionCheck
{
    public string Resource { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
}

public class PermissionEvaluationResult
{
    public string UserId { get; set; } = string.Empty;
    public List<Permission> GrantedPermissions { get; set; } = new();
    public List<PermissionDenial> DeniedPermissions { get; set; } = new();
    public DateTime EvaluatedAt { get; set; }
}

public class PermissionDenial
{
    public string Resource { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}