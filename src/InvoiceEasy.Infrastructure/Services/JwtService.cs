using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using InvoiceEasy.Domain.Entities;
using Microsoft.Extensions.Configuration;

namespace InvoiceEasy.Infrastructure.Services;

public class JwtService
{
    private readonly string _secretKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly TimeSpan _accessTokenLifetime;
    private readonly int _refreshTokenDays;

    public JwtService(IConfiguration configuration)
    {
        _secretKey = configuration["JWT_SECRET"] ?? throw new InvalidOperationException("JWT_SECRET not configured");
        _issuer = configuration["JWT_ISSUER"] ?? "InvoiceEasy";
        _audience = configuration["JWT_AUDIENCE"] ?? "InvoiceEasy";
        _accessTokenLifetime = ResolveAccessTokenLifetime(configuration);
        _refreshTokenDays = int.Parse(configuration["JWT_REFRESH_TOKEN_TTL"] ?? "30");
    }

    public string GenerateAccessToken(User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim("plan", user.Plan)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(_accessTokenLifetime),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var randomNumber = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }

    public ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
    {
        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = false,
            ValidateIssuer = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey)),
            ValidateLifetime = false
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out SecurityToken securityToken);
        
        if (securityToken is not JwtSecurityToken jwtSecurityToken ||
            !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
        {
            throw new SecurityTokenException("Invalid token");
        }

        return principal;
    }

    public int GetRefreshTokenDays() => _refreshTokenDays;
    private static TimeSpan ResolveAccessTokenLifetime(IConfiguration configuration)
    {
        var ttlSecondsRaw = configuration["JWT_ACCESS_TOKEN_TTL_SECONDS"];
        if (!string.IsNullOrWhiteSpace(ttlSecondsRaw) && double.TryParse(ttlSecondsRaw, out var seconds) && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        var ttlMinutesRaw = configuration["JWT_ACCESS_TOKEN_TTL"] ?? "15";
        if (!double.TryParse(ttlMinutesRaw, out var minutes) || minutes <= 0)
        {
            minutes = 15;
        }

        return TimeSpan.FromMinutes(minutes);
    }
}
