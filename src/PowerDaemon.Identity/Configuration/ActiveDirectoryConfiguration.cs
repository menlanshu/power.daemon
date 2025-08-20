using System.Text.Json.Serialization;

namespace PowerDaemon.Identity.Configuration;

public class ActiveDirectoryConfiguration
{
    [JsonPropertyName("domain")]
    public string Domain { get; set; } = string.Empty;

    [JsonPropertyName("ldapServer")]
    public string LdapServer { get; set; } = string.Empty;

    [JsonPropertyName("ldapPort")]
    public int LdapPort { get; set; } = 389;

    [JsonPropertyName("useSsl")]
    public bool UseSsl { get; set; } = true;

    [JsonPropertyName("sslPort")]
    public int SslPort { get; set; } = 636;

    [JsonPropertyName("bindUserName")]
    public string BindUserName { get; set; } = string.Empty;

    [JsonPropertyName("bindPassword")]
    public string BindPassword { get; set; } = string.Empty;

    [JsonPropertyName("baseDn")]
    public string BaseDn { get; set; } = string.Empty;

    [JsonPropertyName("userSearchBase")]
    public string UserSearchBase { get; set; } = string.Empty;

    [JsonPropertyName("groupSearchBase")]
    public string GroupSearchBase { get; set; } = string.Empty;

    [JsonPropertyName("userObjectClass")]
    public string UserObjectClass { get; set; } = "user";

    [JsonPropertyName("groupObjectClass")]
    public string GroupObjectClass { get; set; } = "group";

    [JsonPropertyName("userNameAttribute")]
    public string UserNameAttribute { get; set; } = "sAMAccountName";

    [JsonPropertyName("emailAttribute")]
    public string EmailAttribute { get; set; } = "mail";

    [JsonPropertyName("displayNameAttribute")]
    public string DisplayNameAttribute { get; set; } = "displayName";

    [JsonPropertyName("memberOfAttribute")]
    public string MemberOfAttribute { get; set; } = "memberOf";

    [JsonPropertyName("groupNameAttribute")]
    public string GroupNameAttribute { get; set; } = "cn";

    [JsonPropertyName("connectionTimeout")]
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    [JsonPropertyName("searchTimeout")]
    public TimeSpan SearchTimeout { get; set; } = TimeSpan.FromSeconds(30);

    [JsonPropertyName("enableUserGroups")]
    public bool EnableUserGroups { get; set; } = true;

    [JsonPropertyName("enableNestedGroups")]
    public bool EnableNestedGroups { get; set; } = true;

    [JsonPropertyName("maxSearchResults")]
    public int MaxSearchResults { get; set; } = 1000;

    [JsonPropertyName("cacheConfiguration")]
    public ActiveDirectoryCacheConfiguration CacheConfiguration { get; set; } = new();

    [JsonPropertyName("fallbackConfiguration")]
    public FallbackConfiguration FallbackConfiguration { get; set; } = new();

    [JsonPropertyName("customAttributes")]
    public List<CustomAttribute> CustomAttributes { get; set; } = new();

    [JsonPropertyName("groupMapping")]
    public Dictionary<string, string> GroupMapping { get; set; } = new();

    [JsonPropertyName("userFilters")]
    public List<string> UserFilters { get; set; } = new();

    [JsonPropertyName("groupFilters")]
    public List<string> GroupFilters { get; set; } = new();
}

public class ActiveDirectoryCacheConfiguration
{
    [JsonPropertyName("enableCaching")]
    public bool EnableCaching { get; set; } = true;

    [JsonPropertyName("userCacheTtl")]
    public TimeSpan UserCacheTtl { get; set; } = TimeSpan.FromMinutes(30);

    [JsonPropertyName("groupCacheTtl")]
    public TimeSpan GroupCacheTtl { get; set; } = TimeSpan.FromHours(1);

    [JsonPropertyName("authenticationCacheTtl")]
    public TimeSpan AuthenticationCacheTtl { get; set; } = TimeSpan.FromMinutes(15);

    [JsonPropertyName("maxCacheEntries")]
    public int MaxCacheEntries { get; set; } = 10000;

    [JsonPropertyName("cacheKeyPrefix")]
    public string CacheKeyPrefix { get; set; } = "ad:";
}

public class FallbackConfiguration
{
    [JsonPropertyName("enableFallback")]
    public bool EnableFallback { get; set; } = true;

    [JsonPropertyName("fallbackServers")]
    public List<string> FallbackServers { get; set; } = new();

    [JsonPropertyName("fallbackTimeout")]
    public TimeSpan FallbackTimeout { get; set; } = TimeSpan.FromSeconds(10);

    [JsonPropertyName("maxRetryAttempts")]
    public int MaxRetryAttempts { get; set; } = 3;

    [JsonPropertyName("retryDelay")]
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);
}

public class CustomAttribute
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("ldapAttribute")]
    public string LdapAttribute { get; set; } = string.Empty;

    [JsonPropertyName("dataType")]
    public AttributeDataType DataType { get; set; } = AttributeDataType.String;

    [JsonPropertyName("required")]
    public bool Required { get; set; } = false;

    [JsonPropertyName("multiValue")]
    public bool MultiValue { get; set; } = false;
}

public enum AttributeDataType
{
    String,
    Integer,
    Boolean,
    DateTime,
    Binary
}