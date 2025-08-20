using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PowerDaemon.Identity.Configuration;
using PowerDaemon.Identity.Models;
using PowerDaemon.Cache.Services;

namespace PowerDaemon.Identity.Services;

public interface IJwtTokenService
{
    Task<string> GenerateAccessTokenAsync(User user, AuthenticationOptions? options = null, CancellationToken cancellationToken = default);
    Task<string> GenerateRefreshTokenAsync(User user, CancellationToken cancellationToken = default);
    Task<PowerDaemonTokenValidationResult> ValidateTokenAsync(string token, CancellationToken cancellationToken = default);
    Task<ClaimsPrincipal?> GetPrincipalFromTokenAsync(string token, bool validateLifetime = true, CancellationToken cancellationToken = default);
    Task<bool> RevokeTokenAsync(string token, CancellationToken cancellationToken = default);
    Task<bool> IsTokenRevokedAsync(string token, CancellationToken cancellationToken = default);
    Task<AuthenticationResult> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
}

public class JwtTokenService : IJwtTokenService
{
    private readonly ILogger<JwtTokenService> _logger;
    private readonly JwtConfiguration _config;
    private readonly ICacheService _cacheService;
    private readonly IActiveDirectoryService _activeDirectoryService;
    private readonly JwtSecurityTokenHandler _tokenHandler;
    private readonly TokenValidationParameters _tokenValidationParameters;

    public JwtTokenService(
        ILogger<JwtTokenService> logger,
        IOptions<JwtConfiguration> config,
        ICacheService cacheService,
        IActiveDirectoryService activeDirectoryService)
    {
        _logger = logger;
        _config = config.Value;
        _cacheService = cacheService;
        _activeDirectoryService = activeDirectoryService;
        _tokenHandler = new JwtSecurityTokenHandler();
        
        _tokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = _config.ValidateIssuer,
            ValidIssuer = _config.Issuer,
            ValidateAudience = _config.ValidateAudience,
            ValidAudience = _config.Audience,
            ValidateLifetime = _config.ValidateLifetime,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config.SecretKey)),
            ValidateIssuerSigningKey = _config.ValidateIssuerSigningKey,
            RequireExpirationTime = _config.RequireExpirationTime,
            RequireSignedTokens = _config.RequireSignedTokens,
            ClockSkew = _config.ClockSkew
        };
    }

    public async Task<string> GenerateAccessTokenAsync(User user, AuthenticationOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Generating access token for user {UserName}", user.UserName);

            var tokenExpiration = options?.TokenExpiration ?? _config.AccessTokenExpiration;
            var now = DateTime.UtcNow;
            var expires = now.Add(tokenExpiration);

            var claims = await BuildClaimsAsync(user, options);

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config.SecretKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = expires,
                Issuer = _config.Issuer,
                Audience = _config.Audience,
                SigningCredentials = credentials
            };

            // Add encryption if enabled
            if (_config.TokenEncryption.EnableEncryption)
            {
                var encryptionKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config.TokenEncryption.EncryptionKey));
                tokenDescriptor.EncryptingCredentials = new EncryptingCredentials(
                    encryptionKey,
                    _config.TokenEncryption.KeyDerivationAlgorithm,
                    _config.TokenEncryption.EncryptionAlgorithm);
            }

            var token = _tokenHandler.CreateToken(tokenDescriptor);
            var tokenString = _tokenHandler.WriteToken(token);

            // Cache token metadata
            var tokenCacheKey = $"token:{GetTokenId(tokenString)}";
            var tokenMetadata = new TokenMetadata
            {
                UserId = user.Id,
                UserName = user.UserName,
                IssuedAt = now,
                ExpiresAt = expires,
                TokenType = "access",
                IsRevoked = false
            };

            await _cacheService.SetAsync(tokenCacheKey, tokenMetadata, tokenExpiration.Add(TimeSpan.FromMinutes(5)));

            _logger.LogDebug("Access token generated successfully for user {UserName}", user.UserName);
            return tokenString;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating access token for user {UserName}", user.UserName);
            throw;
        }
    }

    public async Task<string> GenerateRefreshTokenAsync(User user, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Generating refresh token for user {UserName}", user.UserName);

            var now = DateTime.UtcNow;
            var expires = now.Add(_config.RefreshTokenExpiration);
            var tokenId = Guid.NewGuid().ToString();

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id),
                new(JwtRegisteredClaimNames.Jti, tokenId),
                new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new("token_type", "refresh"),
                new("user_name", user.UserName)
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config.SecretKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = expires,
                Issuer = _config.Issuer,
                Audience = _config.Audience,
                SigningCredentials = credentials
            };

            var token = _tokenHandler.CreateToken(tokenDescriptor);
            var tokenString = _tokenHandler.WriteToken(token);

            // Cache refresh token metadata
            var tokenCacheKey = $"refresh_token:{tokenId}";
            var tokenMetadata = new RefreshTokenMetadata
            {
                UserId = user.Id,
                UserName = user.UserName,
                TokenId = tokenId,
                IssuedAt = now,
                ExpiresAt = expires,
                IsRevoked = false,
                LastUsed = now
            };

            await _cacheService.SetAsync(tokenCacheKey, tokenMetadata, _config.RefreshTokenExpiration.Add(TimeSpan.FromDays(1)));

            _logger.LogDebug("Refresh token generated successfully for user {UserName}", user.UserName);
            return tokenString;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating refresh token for user {UserName}", user.UserName);
            throw;
        }
    }

    public async Task<PowerDaemonTokenValidationResult> ValidateTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Validating token");

            // Check if token is revoked
            if (await IsTokenRevokedAsync(token, cancellationToken))
            {
                _logger.LogWarning("Token validation failed: token is revoked");
                return new PowerDaemonTokenValidationResult
                {
                    IsValid = false,
                    ErrorCode = "TokenRevoked",
                    ErrorDescription = "Token has been revoked"
                };
            }

            var principal = await GetPrincipalFromTokenAsync(token, true, cancellationToken);
            if (principal == null)
            {
                return new PowerDaemonTokenValidationResult
                {
                    IsValid = false,
                    ErrorCode = "InvalidToken",
                    ErrorDescription = "Token is invalid or expired"
                };
            }

            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? 
                        principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            
            if (string.IsNullOrEmpty(userId))
            {
                return new PowerDaemonTokenValidationResult
                {
                    IsValid = false,
                    ErrorCode = "InvalidClaims",
                    ErrorDescription = "Token does not contain valid user identifier"
                };
            }

            // Get user information
            var userName = principal.FindFirst("user_name")?.Value ?? 
                          principal.FindFirst(ClaimTypes.Name)?.Value;
            
            User? user = null;
            if (!string.IsNullOrEmpty(userName))
            {
                user = await _activeDirectoryService.GetUserAsync(userName, cancellationToken);
            }

            var expClaim = principal.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;
            DateTime? expiresAt = null;
            if (!string.IsNullOrEmpty(expClaim) && long.TryParse(expClaim, out var exp))
            {
                expiresAt = DateTimeOffset.FromUnixTimeSeconds(exp).DateTime;
            }

            var claims = principal.Claims.ToDictionary(c => c.Type, c => (object)c.Value);

            _logger.LogDebug("Token validated successfully for user {UserName}", userName);
            return new PowerDaemonTokenValidationResult
            {
                IsValid = true,
                User = user,
                Claims = claims,
                ExpiresAt = expiresAt,
                IsExpired = expiresAt.HasValue && expiresAt.Value < DateTime.UtcNow
            };
        }
        catch (SecurityTokenExpiredException)
        {
            _logger.LogDebug("Token validation failed: token expired");
            return new PowerDaemonTokenValidationResult
            {
                IsValid = false,
                IsExpired = true,
                ErrorCode = "TokenExpired",
                ErrorDescription = "Token has expired"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating token");
            return new PowerDaemonTokenValidationResult
            {
                IsValid = false,
                ErrorCode = "ValidationError",
                ErrorDescription = "An error occurred while validating the token"
            };
        }
    }

    public async Task<ClaimsPrincipal?> GetPrincipalFromTokenAsync(string token, bool validateLifetime = true, CancellationToken cancellationToken = default)
    {
        try
        {
            var validationParameters = _tokenValidationParameters.Clone();
            validationParameters.ValidateLifetime = validateLifetime;

            var principal = _tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);
            return principal;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get principal from token");
            return null;
        }
    }

    public async Task<bool> RevokeTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Revoking token");

            var tokenId = GetTokenId(token);
            var cacheKey = $"revoked_token:{tokenId}";
            
            // Get token expiration to set appropriate TTL
            var principal = await GetPrincipalFromTokenAsync(token, false, cancellationToken);
            var expClaim = principal?.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;
            
            TimeSpan ttl = _config.AccessTokenExpiration;
            if (!string.IsNullOrEmpty(expClaim) && long.TryParse(expClaim, out var exp))
            {
                var expiresAt = DateTimeOffset.FromUnixTimeSeconds(exp).DateTime;
                var remainingTime = expiresAt - DateTime.UtcNow;
                if (remainingTime > TimeSpan.Zero)
                {
                    ttl = remainingTime.Add(TimeSpan.FromMinutes(5)); // Add small buffer
                }
            }

            await _cacheService.SetAsync(cacheKey, "revoked", ttl);

            _logger.LogDebug("Token revoked successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking token");
            return false;
        }
    }

    public async Task<bool> IsTokenRevokedAsync(string token, CancellationToken cancellationToken = default)
    {
        try
        {
            var tokenId = GetTokenId(token);
            var cacheKey = $"revoked_token:{tokenId}";
            
            var revokedValue = await _cacheService.GetAsync<string>(cacheKey);
            return !string.IsNullOrEmpty(revokedValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking token revocation status");
            return false; // Default to not revoked if we can't check
        }
    }

    public async Task<AuthenticationResult> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Refreshing token");

            var principal = await GetPrincipalFromTokenAsync(refreshToken, true, cancellationToken);
            if (principal == null)
            {
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorCode = "InvalidRefreshToken",
                    ErrorDescription = "Refresh token is invalid or expired"
                };
            }

            var tokenType = principal.FindFirst("token_type")?.Value;
            if (tokenType != "refresh")
            {
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorCode = "InvalidTokenType",
                    ErrorDescription = "Token is not a refresh token"
                };
            }

            var tokenId = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
            if (string.IsNullOrEmpty(tokenId))
            {
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorCode = "InvalidToken",
                    ErrorDescription = "Refresh token does not contain valid token ID"
                };
            }

            // Check if refresh token is revoked
            var refreshTokenCacheKey = $"refresh_token:{tokenId}";
            var tokenMetadata = await _cacheService.GetAsync<RefreshTokenMetadata>(refreshTokenCacheKey);
            
            if (tokenMetadata == null || tokenMetadata.IsRevoked)
            {
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorCode = "TokenRevoked",
                    ErrorDescription = "Refresh token has been revoked"
                };
            }

            var userName = principal.FindFirst("user_name")?.Value;
            if (string.IsNullOrEmpty(userName))
            {
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorCode = "InvalidClaims",
                    ErrorDescription = "Refresh token does not contain valid user name"
                };
            }

            // Get current user data
            var user = await _activeDirectoryService.GetUserAsync(userName, cancellationToken);
            if (user == null)
            {
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorCode = "UserNotFound",
                    ErrorDescription = "User not found"
                };
            }

            if (!user.IsEnabled)
            {
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorCode = "AccountDisabled",
                    ErrorDescription = "User account is disabled"
                };
            }

            // Generate new access token
            var newAccessToken = await GenerateAccessTokenAsync(user, null, cancellationToken);
            
            // Update refresh token last used timestamp
            tokenMetadata.LastUsed = DateTime.UtcNow;
            await _cacheService.SetAsync(refreshTokenCacheKey, tokenMetadata, _config.RefreshTokenExpiration);

            var expiresAt = DateTime.UtcNow.Add(_config.AccessTokenExpiration);

            _logger.LogDebug("Token refreshed successfully for user {UserName}", userName);
            return new AuthenticationResult
            {
                Success = true,
                User = user,
                AccessToken = newAccessToken,
                RefreshToken = refreshToken, // Return the same refresh token
                TokenType = "Bearer",
                ExpiresIn = (int)_config.AccessTokenExpiration.TotalSeconds,
                ExpiresAt = expiresAt,
                AuthenticationMethod = "RefreshToken",
                AuthenticatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing token");
            return new AuthenticationResult
            {
                Success = false,
                ErrorCode = "RefreshTokenError",
                ErrorDescription = "An error occurred while refreshing the token"
            };
        }
    }

    private async Task<List<Claim>> BuildClaimsAsync(User user, AuthenticationOptions? options)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.GivenName, user.FirstName),
            new(ClaimTypes.Surname, user.LastName),
            new("display_name", user.DisplayName),
            new("user_name", user.UserName),
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        // Add role claims if enabled
        if (_config.IncludeRoleClaims)
        {
            foreach (var role in user.Roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role.Name));
            }
        }

        // Add group claims if enabled
        if (_config.IncludeGroupClaims)
        {
            foreach (var group in user.Groups)
            {
                claims.Add(new Claim("group", group.Name));
            }
        }

        // Add custom claims
        if (_config.IncludeUserClaims)
        {
            if (!string.IsNullOrEmpty(user.Department))
                claims.Add(new Claim("department", user.Department));
            
            if (!string.IsNullOrEmpty(user.Title))
                claims.Add(new Claim("title", user.Title));
            
            if (!string.IsNullOrEmpty(user.Office))
                claims.Add(new Claim("office", user.Office));
        }

        // Add configured custom claims
        foreach (var customClaim in _config.CustomClaims)
        {
            if (user.CustomAttributes.TryGetValue(customClaim, out var value))
            {
                claims.Add(new Claim(customClaim, value?.ToString() ?? string.Empty));
            }
        }

        // Add claims from authentication options
        if (options?.CustomClaims != null)
        {
            foreach (var kvp in options.CustomClaims)
            {
                claims.Add(new Claim(kvp.Key, kvp.Value?.ToString() ?? string.Empty));
            }
        }

        return claims;
    }

    private static string GetTokenId(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadJwtToken(token);
            return jsonToken.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value ?? 
                   Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        }
        catch
        {
            // Fallback to hash of the token
            return Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        }
    }
}

public class TokenMetadata
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string TokenType { get; set; } = string.Empty;
    public bool IsRevoked { get; set; }
}

public class RefreshTokenMetadata
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string TokenId { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime LastUsed { get; set; }
    public bool IsRevoked { get; set; }
}