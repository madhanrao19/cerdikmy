using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Cerdik.Application.Abstractions;
using Cerdik.Domain;
using Microsoft.AspNetCore.Http;

namespace Cerdik.Infrastructure.Auth;

/// <summary>Reads the authenticated principal from the current HTTP request.</summary>
public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId => GetGuid(JwtRegisteredClaimNames.Sub) ?? GetGuid(ClaimTypes.NameIdentifier);

    public Guid? OrganizationId => GetGuid(TokenService.OrgClaim);

    public Guid? StudentId => GetGuid(TokenService.StudentClaim);

    public UserRole? Role =>
        Enum.TryParse<UserRole>(Principal?.FindFirstValue(ClaimTypes.Role), out var r) ? r : null;

    public string? Email => Principal?.FindFirstValue(JwtRegisteredClaimNames.Email)
                            ?? Principal?.FindFirstValue(ClaimTypes.Email);

    public bool IsInRole(params UserRole[] roles) => Role is { } r && roles.Contains(r);

    private Guid? GetGuid(string claim) =>
        Guid.TryParse(Principal?.FindFirstValue(claim), out var g) ? g : null;
}
