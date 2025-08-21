using AutoFixture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PowerDaemon.Cache.Services;
using PowerDaemon.Identity.Configuration;
using PowerDaemon.Identity.Models;
using PowerDaemon.Identity.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Xunit;

namespace PowerDaemon.Tests.Unit.Identity;

public class JwtTokenServiceTests
{
    private readonly JwtTokenService _tokenService;
    private readonly ILogger<JwtTokenService> _logger;
    private readonly JwtConfiguration _jwtConfig;
    private readonly ICacheService _cacheService;
    private readonly IActiveDirectoryService _activeDirectoryService;
    private readonly Fixture _fixture = new();

    public JwtTokenServiceTests()
    {
        _logger = Substitute.For<ILogger<JwtTokenService>>();
        _cacheService = Substitute.For<ICacheService>();
        _activeDirectoryService = Substitute.For<IActiveDirectoryService>();

        _jwtConfig = new JwtConfiguration
        {
            Secret = "ThisIsAVerySecureSecretKeyThatIsAtLeast256BitsLongForHS256Algorithm",
            Issuer = "PowerDaemon.Identity",
            Audience = "PowerDaemon.Services",
            ExpiryMinutes = 60,
            RefreshTokenExpiryDays = 30,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireExpirationTime = true,
            SaveTokenInCache = true,
            TokenCacheExpiryMinutes = 65
        };

        var options = Substitute.For<IOptions<JwtConfiguration>>();
        options.Value.Returns(_jwtConfig);

        _tokenService = new JwtTokenService(_logger, options, _cacheService, _activeDirectoryService);
    }

    [Fact]
    public async Task GenerateAccessTokenAsync_ValidUser_GeneratesToken()
    {
        // Arrange
        var user = _fixture.Build<User>()
            .With(u => u.Username, "testuser")
            .With(u => u.Email, "test@company.com")
            .With(u => u.Roles, new List<string> { "User", "Deployer" })
            .Create();

        // Act
        var token = await _tokenService.GenerateAccessTokenAsync(user);

        // Assert
        token.Should().NotBeNullOrEmpty();
        
        var tokenHandler = new JwtSecurityTokenHandler();
        tokenHandler.CanReadToken(token).Should().BeTrue();
        
        var jwtToken = tokenHandler.ReadJwtToken(token);
        jwtToken.Issuer.Should().Be(_jwtConfig.Issuer);
        jwtToken.Audiences.Should().Contain(_jwtConfig.Audience);
        
        var usernameClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);
        usernameClaim.Should().NotBeNull();
        usernameClaim!.Value.Should().Be("testuser");
    }

    [Fact]
    public async Task GenerateAccessTokenAsync_NullUser_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => _tokenService.GenerateAccessTokenAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("user");
    }

    [Fact]
    public async Task GenerateAccessTokenAsync_UserWithMultipleRoles_IncludesAllRoles()
    {
        // Arrange
        var user = _fixture.Build<User>()
            .With(u => u.Username, "adminuser")
            .With(u => u.Roles, new List<string> { "User", "Administrator", "Deployer", "Auditor" })
            .Create();

        // Act
        var token = await _tokenService.GenerateAccessTokenAsync(user);

        // Assert
        var tokenHandler = new JwtSecurityTokenHandler();
        var jwtToken = tokenHandler.ReadJwtToken(token);
        
        var roleClaims = jwtToken.Claims.Where(c => c.Type == ClaimTypes.Role).ToList();
        roleClaims.Should().HaveCount(4);
        roleClaims.Select(c => c.Value).Should().BeEquivalentTo(new[] { "User", "Administrator", "Deployer", "Auditor" });
    }

    [Fact]
    public async Task GenerateRefreshTokenAsync_ValidUser_GeneratesRefreshToken()
    {
        // Arrange
        var user = _fixture.Create<User>();

        // Act
        var refreshToken = await _tokenService.GenerateRefreshTokenAsync(user);

        // Assert
        refreshToken.Should().NotBeNullOrEmpty();
        refreshToken.Length.Should().BeGreaterThan(32, "Refresh token should be sufficiently long");
    }

    [Fact]
    public async Task GenerateRefreshTokenAsync_NullUser_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => _tokenService.GenerateRefreshTokenAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("user");
    }

    [Fact]
    public async Task ValidateTokenAsync_ValidToken_ReturnsSuccessResult()
    {
        // Arrange
        var user = _fixture.Build<User>()
            .With(u => u.Username, "testuser")
            .Create();
        
        var token = await _tokenService.GenerateAccessTokenAsync(user);

        // Act
        var result = await _tokenService.ValidateTokenAsync(token);

        // Assert
        result.Should().NotBeNull();
        result.IsValid.Should().BeTrue();
        result.Principal.Should().NotBeNull();
        result.Principal!.Identity!.Name.Should().Be("testuser");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task ValidateTokenAsync_InvalidToken_ReturnsFailureResult(string invalidToken)
    {
        // Act
        var result = await _tokenService.ValidateTokenAsync(invalidToken!);

        // Assert
        result.Should().NotBeNull();
        result.IsValid.Should().BeFalse();
        result.Principal.Should().BeNull();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ValidateTokenAsync_ExpiredToken_ReturnsFailureResult()
    {
        // Arrange - Create configuration with very short expiry
        var shortExpiryConfig = new JwtConfiguration
        {
            Secret = _jwtConfig.Secret,
            Issuer = _jwtConfig.Issuer,
            Audience = _jwtConfig.Audience,
            ExpiryMinutes = -1, // Already expired
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true
        };

        var options = Substitute.For<IOptions<JwtConfiguration>>();
        options.Value.Returns(shortExpiryConfig);

        var tokenServiceWithShortExpiry = new JwtTokenService(_logger, options, _cacheService, _activeDirectoryService);
        
        var user = _fixture.Create<User>();
        var expiredToken = await tokenServiceWithShortExpiry.GenerateAccessTokenAsync(user);

        // Act
        var result = await _tokenService.ValidateTokenAsync(expiredToken);

        // Assert
        result.Should().NotBeNull();
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("lifetime");
    }

    [Fact]
    public async Task GetPrincipalFromTokenAsync_ValidToken_ReturnsPrincipal()
    {
        // Arrange
        var user = _fixture.Build<User>()
            .With(u => u.Username, "principaluser")
            .With(u => u.Email, "principal@company.com")
            .Create();
        
        var token = await _tokenService.GenerateAccessTokenAsync(user);

        // Act
        var principal = await _tokenService.GetPrincipalFromTokenAsync(token);

        // Assert
        principal.Should().NotBeNull();
        principal!.Identity!.Name.Should().Be("principaluser");
        principal.HasClaim(ClaimTypes.Email, "principal@company.com").Should().BeTrue();
    }

    [Fact]
    public async Task GetPrincipalFromTokenAsync_InvalidToken_ReturnsNull()
    {
        // Act
        var principal = await _tokenService.GetPrincipalFromTokenAsync("invalid.token.here");

        // Assert
        principal.Should().BeNull();
    }

    [Fact]
    public async Task RevokeTokenAsync_ValidToken_RevokesSuccessfully()
    {
        // Arrange
        var user = _fixture.Create<User>();
        var token = await _tokenService.GenerateAccessTokenAsync(user);

        _cacheService.SetAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _tokenService.RevokeTokenAsync(token);

        // Assert
        result.Should().BeTrue();
        
        // Verify token was added to revocation cache
        await _cacheService.Received(1).SetAsync(
            Arg.Is<string>(key => key.Contains("revoked")), 
            Arg.Any<object>(), 
            Arg.Any<TimeSpan?>(), 
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task RevokeTokenAsync_InvalidToken_ReturnsFalse(string invalidToken)
    {
        // Act
        var result = await _tokenService.RevokeTokenAsync(invalidToken!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsTokenRevokedAsync_RevokedToken_ReturnsTrue()
    {
        // Arrange
        var user = _fixture.Create<User>();
        var token = await _tokenService.GenerateAccessTokenAsync(user);
        
        _cacheService.SetAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(true);
        
        _cacheService.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // First revoke the token
        await _tokenService.RevokeTokenAsync(token);

        // Act
        var isRevoked = await _tokenService.IsTokenRevokedAsync(token);

        // Assert
        isRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task IsTokenRevokedAsync_ValidToken_ReturnsFalse()
    {
        // Arrange
        var user = _fixture.Create<User>();
        var token = await _tokenService.GenerateAccessTokenAsync(user);
        
        _cacheService.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var isRevoked = await _tokenService.IsTokenRevokedAsync(token);

        // Assert
        isRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshTokenAsync_ValidRefreshToken_GeneratesNewAccessToken()
    {
        // Arrange
        var user = _fixture.Build<User>()
            .With(u => u.Username, "refreshuser")
            .Create();
        
        var refreshToken = await _tokenService.GenerateRefreshTokenAsync(user);
        
        _cacheService.GetAsync<User>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(user);

        // Act
        var result = await _tokenService.RefreshTokenAsync(refreshToken);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        
        // Verify new access token is valid
        var tokenHandler = new JwtSecurityTokenHandler();
        tokenHandler.CanReadToken(result.AccessToken).Should().BeTrue();
        
        var jwtToken = tokenHandler.ReadJwtToken(result.AccessToken);
        var usernameClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);
        usernameClaim?.Value.Should().Be("refreshuser");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task RefreshTokenAsync_InvalidRefreshToken_ReturnsFailure(string invalidRefreshToken)
    {
        // Act
        var result = await _tokenService.RefreshTokenAsync(invalidRefreshToken!);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RefreshTokenAsync_ExpiredRefreshToken_ReturnsFailure()
    {
        // Arrange
        var refreshToken = "expired.refresh.token";
        
        _cacheService.GetAsync<User>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((User?)null);

        // Act
        var result = await _tokenService.RefreshTokenAsync(refreshToken);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GenerateAccessTokenAsync_ProductionScaleUser_HandlesLargeClaimsSet()
    {
        // Arrange - User with many roles and permissions (production scenario)
        var user = _fixture.Build<User>()
            .With(u => u.Username, "enterprise.admin")
            .With(u => u.Email, "admin@enterprise.com")
            .With(u => u.Roles, new List<string>
            {
                "Administrator", "Deployer", "Auditor", "SecurityOfficer",
                "DatabaseAdmin", "NetworkAdmin", "MonitoringAdmin",
                "BackupOperator", "HelpDesk", "Developer"
            })
            .Create();

        // Act
        var token = await _tokenService.GenerateAccessTokenAsync(user);

        // Assert
        token.Should().NotBeNullOrEmpty();
        
        var tokenHandler = new JwtSecurityTokenHandler();
        var jwtToken = tokenHandler.ReadJwtToken(token);
        
        var roleClaims = jwtToken.Claims.Where(c => c.Type == ClaimTypes.Role).ToList();
        roleClaims.Should().HaveCount(10);
        
        // Verify token is still valid despite large claims set
        var validationResult = await _tokenService.ValidateTokenAsync(token);
        validationResult.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task GenerateAccessTokenAsync_CustomAuthenticationOptions_AppliesOptions()
    {
        // Arrange
        var user = _fixture.Create<User>();
        var customOptions = new AuthenticationOptions
        {
            ExpiryOverrideMinutes = 120,
            IncludeRefreshToken = true,
            CustomClaims = new Dictionary<string, string>
            {
                ["department"] = "IT",
                ["clearance"] = "Level3"
            }
        };

        // Act
        var token = await _tokenService.GenerateAccessTokenAsync(user, customOptions);

        // Assert
        token.Should().NotBeNullOrEmpty();
        
        var tokenHandler = new JwtSecurityTokenHandler();
        var jwtToken = tokenHandler.ReadJwtToken(token);
        
        // Check for custom claims
        jwtToken.Claims.Should().Contain(c => c.Type == "department" && c.Value == "IT");
        jwtToken.Claims.Should().Contain(c => c.Type == "clearance" && c.Value == "Level3");
        
        // Check extended expiry (should be closer to 2 hours than 1 hour)
        var expiryTime = jwtToken.ValidTo;
        var creationTime = jwtToken.ValidFrom;
        var tokenLifetime = expiryTime - creationTime;
        tokenLifetime.Should().BeCloseTo(TimeSpan.FromMinutes(120), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task TokenCaching_ValidConfiguration_CachesTokens()
    {
        // Arrange
        var user = _fixture.Create<User>();
        _jwtConfig.SaveTokenInCache = true;

        // Act
        var token = await _tokenService.GenerateAccessTokenAsync(user);

        // Assert - Should attempt to cache the token
        await _cacheService.Received().SetAsync(
            Arg.Is<string>(key => key.Contains("token")), 
            Arg.Any<object>(), 
            Arg.Is<TimeSpan>(expiry => expiry.TotalMinutes >= _jwtConfig.TokenCacheExpiryMinutes),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(25)]
    [InlineData(50)]
    public async Task ConcurrentTokenGeneration_MultipleUsers_HandlesParallelRequests(int userCount)
    {
        // Arrange
        var users = _fixture.CreateMany<User>(userCount).ToList();
        var tasks = new List<Task<string>>();

        // Act
        foreach (var user in users)
        {
            tasks.Add(_tokenService.GenerateAccessTokenAsync(user));
        }

        var tokens = await Task.WhenAll(tasks);

        // Assert
        tokens.Should().HaveCount(userCount);
        tokens.Should().OnlyHaveUniqueItems("Each token should be unique");
        tokens.Should().OnlyContain(t => !string.IsNullOrEmpty(t), "All tokens should be valid");
    }

    [Fact]
    public void JwtConfiguration_ProductionSettings_AreSecure()
    {
        // Assert
        _jwtConfig.Secret.Length.Should().BeGreaterThan(32, "Secret should be sufficiently long");
        _jwtConfig.ValidateIssuer.Should().BeTrue("Should validate issuer in production");
        _jwtConfig.ValidateAudience.Should().BeTrue("Should validate audience in production");
        _jwtConfig.ValidateLifetime.Should().BeTrue("Should validate token lifetime");
        _jwtConfig.ValidateIssuerSigningKey.Should().BeTrue("Should validate signing key");
        _jwtConfig.RequireExpirationTime.Should().BeTrue("Tokens should have expiration");
        _jwtConfig.ExpiryMinutes.Should().BeInRange(15, 480, "Token expiry should be reasonable");
        _jwtConfig.RefreshTokenExpiryDays.Should().BeInRange(1, 90, "Refresh token expiry should be reasonable");
    }
}