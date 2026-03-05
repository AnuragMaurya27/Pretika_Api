using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Modules.Auth.Models;
using Microsoft.IdentityModel.Tokens;

namespace HauntedVoiceUniverse.Modules.Auth.Services;

public interface IJwtService
{
    string GenerateAccessToken(UserAuthInfo user);
    string GenerateRefreshToken();
    string HashToken(string token);
    ClaimsPrincipal? ValidateToken(string token);
    TokenInfo? GetTokenInfo(ClaimsPrincipal principal);
}

public class JwtService : IJwtService
{
    private readonly IConfiguration _config;
    private readonly SymmetricSecurityKey _signingKey;
    private readonly int _accessTokenMinutes;
    private readonly string _issuer;
    private readonly string _audience;

    public JwtService(IConfiguration config)
    {
        _config = config;
        var secret = config["Jwt:SecretKey"] ?? throw new Exception("JWT SecretKey missing");
        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        _accessTokenMinutes = int.Parse(config["Jwt:AccessTokenExpiryMinutes"] ?? "60");
        _issuer = config["Jwt:Issuer"] ?? "HVU";
        _audience = config["Jwt:Audience"] ?? "HVUUsers";
    }

    /// <summary>Access token generate karo - 60 min expiry</summary>
    public string GenerateAccessToken(UserAuthInfo user)
    {
        // ✅ HvuClaims use kar rahe hain - System.Security.Claims.ClaimTypes se conflict nahi
        var claims = new List<Claim>
        {
            new(HvuClaims.UserId,    user.Id.ToString()),
            new(HvuClaims.Email,     user.Email),
            new(HvuClaims.Role,      user.Role),
            new(HvuClaims.Username,  user.Username),
            new(HvuClaims.IsCreator, user.IsCreator.ToString().ToLower()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

        var creds = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_accessTokenMinutes),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Secure random refresh token</summary>
    public string GenerateRefreshToken()
    {
        var bytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>Refresh token ko hash karke DB mein store karo</summary>
    public string HashToken(string token)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash).ToLower();
    }

    /// <summary>Token validate karo (expiry bhi check hogi)</summary>
    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var validationParams = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _issuer,
                ValidAudience = _audience,
                IssuerSigningKey = _signingKey,
                ClockSkew = TimeSpan.Zero
            };

            return handler.ValidateToken(token, validationParams, out _);
        }
        catch
        {
            return null;
        }
    }

    public TokenInfo? GetTokenInfo(ClaimsPrincipal principal)
    {
        // ✅ HvuClaims use kar rahe hain
        var userIdStr = principal.FindFirstValue(HvuClaims.UserId);
        if (!Guid.TryParse(userIdStr, out var userId)) return null;

        return new TokenInfo
        {
            UserId = userId,
            Role = principal.FindFirstValue(HvuClaims.Role) ?? "",
            Email = principal.FindFirstValue(HvuClaims.Email) ?? "",
            Username = principal.FindFirstValue(HvuClaims.Username) ?? "",
            IsCreator = principal.FindFirstValue(HvuClaims.IsCreator) == "true"
        };
    }
}