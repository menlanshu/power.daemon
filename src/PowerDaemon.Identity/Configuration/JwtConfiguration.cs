using System.Text.Json.Serialization;

namespace PowerDaemon.Identity.Configuration;

public class JwtConfiguration
{
    [JsonPropertyName("secretKey")]
    public string SecretKey { get; set; } = string.Empty;

    [JsonPropertyName("issuer")]
    public string Issuer { get; set; } = "PowerDaemon";

    [JsonPropertyName("audience")]
    public string Audience { get; set; } = "PowerDaemon.Users";

    [JsonPropertyName("accessTokenExpiration")]
    public TimeSpan AccessTokenExpiration { get; set; } = TimeSpan.FromHours(1);

    [JsonPropertyName("refreshTokenExpiration")]
    public TimeSpan RefreshTokenExpiration { get; set; } = TimeSpan.FromDays(7);

    [JsonPropertyName("clockSkew")]
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(5);

    [JsonPropertyName("validateIssuer")]
    public bool ValidateIssuer { get; set; } = true;

    [JsonPropertyName("validateAudience")]
    public bool ValidateAudience { get; set; } = true;

    [JsonPropertyName("validateLifetime")]
    public bool ValidateLifetime { get; set; } = true;

    [JsonPropertyName("validateIssuerSigningKey")]
    public bool ValidateIssuerSigningKey { get; set; } = true;

    [JsonPropertyName("requireExpirationTime")]
    public bool RequireExpirationTime { get; set; } = true;

    [JsonPropertyName("requireSignedTokens")]
    public bool RequireSignedTokens { get; set; } = true;

    [JsonPropertyName("includeUserClaims")]
    public bool IncludeUserClaims { get; set; } = true;

    [JsonPropertyName("includeRoleClaims")]
    public bool IncludeRoleClaims { get; set; } = true;

    [JsonPropertyName("includeGroupClaims")]
    public bool IncludeGroupClaims { get; set; } = true;

    [JsonPropertyName("customClaims")]
    public List<string> CustomClaims { get; set; } = new();

    [JsonPropertyName("tokenEncryption")]
    public TokenEncryptionConfiguration TokenEncryption { get; set; } = new();
}

public class TokenEncryptionConfiguration
{
    [JsonPropertyName("enableEncryption")]
    public bool EnableEncryption { get; set; } = false;

    [JsonPropertyName("encryptionKey")]
    public string EncryptionKey { get; set; } = string.Empty;

    [JsonPropertyName("encryptionAlgorithm")]
    public string EncryptionAlgorithm { get; set; } = "A256GCM";

    [JsonPropertyName("keyDerivationAlgorithm")]
    public string KeyDerivationAlgorithm { get; set; } = "PBES2-HS256+A128KW";
}