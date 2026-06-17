using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Cerdik.Application.Abstractions;
using Cerdik.Domain.Entities;
using Cerdik.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Cerdik.Infrastructure.Auth;

/// <summary>Issues JWT access tokens and opaque (hashed-at-rest) refresh tokens.</summary>
public sealed class TokenService : ITokenService
{
    public const string OrgClaim = "org";
    public const string StudentClaim = "student";

    private readonly JwtOptions _opt;

    public TokenService(IOptions<JwtOptions> opt) => _opt = opt.Value;

    public TokenPair Issue(User user)
    {
        var now = DateTimeOffset.UtcNow;
        var accessExpires = now.AddMinutes(_opt.AccessTtlMinutes);
        var refreshExpires = now.AddDays(_opt.RefreshTtlDays);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(OrgClaim, user.OrganizationId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        if (user.StudentId is { } sid)
        {
            claims.Add(new Claim(StudentClaim, sid.ToString()));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.AccessSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: accessExpires.UtcDateTime,
            signingCredentials: creds);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(jwt);
        var refreshToken = GenerateRefreshToken();
        return new TokenPair(accessToken, refreshToken, accessExpires, refreshExpires);
    }

    public string HashRefreshToken(string rawToken)
    {
        // Keyed hash so a DB leak alone cannot be used to forge tokens.
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_opt.RefreshSecret));
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes);
    }

    private static string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(48);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
