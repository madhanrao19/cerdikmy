using Cerdik.Api.Auth;
using Cerdik.Application.Abstractions;
using Cerdik.Application.Dtos;
using Cerdik.Domain;
using Cerdik.Domain.Entities;
using Cerdik.Infrastructure.Options;
using Cerdik.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cerdik.Api.Controllers;

[ApiController]
[Route("auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly JwtOptions _jwt;
    private readonly IClock _clock;

    public AuthController(AppDbContext db, IPasswordHasher hasher, ITokenService tokens, IOptions<JwtOptions> jwt, IClock clock)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _jwt = jwt.Value;
        _clock = clock;
    }

    [HttpPost("register-parent")]
    public async Task<ActionResult<AuthResponse>> RegisterParent([FromBody] RegisterParentRequest req, CancellationToken ct)
    {
        Validate.Email(req.Email);
        Validate.Password(req.Password);
        if (await _db.Users.AnyAsync(u => u.Email == req.Email, ct))
        {
            throw ApiException.Conflict("An account with this email already exists.");
        }

        var org = new Organization { Name = $"{req.FullName}'s Family", Slug = $"fam-{Guid.NewGuid():N}"[..16] };
        var household = new Household { Organization = org, Name = req.HouseholdName, PreferredLanguage = req.PreferredLanguage };
        var user = new User
        {
            Organization = org,
            Household = household,
            Email = req.Email,
            FullName = req.FullName,
            Role = UserRole.Parent,
            PasswordHash = _hasher.Hash(req.Password),
            EmailConfirmed = false,
        };
        _db.AddRange(org, household, user);
        _db.Consents.Add(new Consent { User = user, Type = ConsentType.DataProcessing, Granted = true });
        await _db.SaveChangesAsync(ct);

        return await IssueAsync(user, ct);
    }

    [HttpPost("register-student")]
    public async Task<ActionResult<AuthResponse>> RegisterStudent([FromBody] RegisterStudentRequest req, CancellationToken ct)
    {
        var household = await _db.Households.Include(h => h.Organization).FirstOrDefaultAsync(h => h.Id == req.HouseholdId, ct)
            ?? throw ApiException.NotFound("Household");

        var student = new Student
        {
            Organization = household.Organization,
            Household = household,
            DisplayName = req.DisplayName,
            Level = req.Level,
            SchoolType = req.SchoolType,
            PrimaryLanguage = req.PrimaryLanguage,
            DlpMode = req.DlpMode,
            DateOfBirth = req.DateOfBirth,
        };
        _db.Students.Add(student);

        User? login = null;
        if (!string.IsNullOrWhiteSpace(req.Email) && !string.IsNullOrWhiteSpace(req.Password))
        {
            Validate.Email(req.Email);
            Validate.Password(req.Password);
            if (await _db.Users.AnyAsync(u => u.Email == req.Email, ct))
            {
                throw ApiException.Conflict("An account with this email already exists.");
            }
            login = new User
            {
                Organization = household.Organization,
                Household = household,
                Email = req.Email!,
                FullName = req.DisplayName,
                Role = UserRole.Student,
                PasswordHash = _hasher.Hash(req.Password!),
                Student = student,
                EmailConfirmed = true,
            };
            _db.Users.Add(login);
        }
        await _db.SaveChangesAsync(ct);

        if (login is null)
        {
            return Ok(new { studentId = student.Id });
        }
        return await IssueAsync(login, ct);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email && u.DeletedAt == null, ct);
        if (user is null || !user.IsActive || !_hasher.Verify(req.Password, user.PasswordHash))
        {
            throw ApiException.Unauthorized("Invalid email or password.");
        }
        user.LastLoginAt = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await IssueAsync(user, ct);
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue(AuthCookies.Refresh, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            throw ApiException.Unauthorized("No refresh token.");
        }
        var hash = _tokens.HashRefreshToken(raw);
        var token = await _db.RefreshTokens.Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null || token.RevokedAt != null || token.ExpiresAt < _clock.UtcNow)
        {
            throw ApiException.Unauthorized("Refresh token expired or revoked.");
        }

        // Rotate.
        token.RevokedAt = _clock.UtcNow;
        var response = await IssueAsync(token.User, ct);
        token.ReplacedByTokenHash = await _db.RefreshTokens
            .Where(t => t.UserId == token.UserId).OrderByDescending(t => t.CreatedAt)
            .Select(t => t.TokenHash).FirstOrDefaultAsync(ct);
        await _db.SaveChangesAsync(ct);
        return response;
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (Request.Cookies.TryGetValue(AuthCookies.Refresh, out var raw) && !string.IsNullOrWhiteSpace(raw))
        {
            var hash = _tokens.HashRefreshToken(raw);
            var token = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
            if (token is not null && token.RevokedAt is null)
            {
                token.RevokedAt = _clock.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
        }
        AuthCookies.Clear(Response);
        return NoContent();
    }

    private async Task<ActionResult<AuthResponse>> IssueAsync(User user, CancellationToken ct)
    {
        var pair = _tokens.Issue(user);
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _tokens.HashRefreshToken(pair.RefreshToken),
            ExpiresAt = pair.RefreshExpiresAt,
            CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
        });
        await _db.SaveChangesAsync(ct);

        AuthCookies.Write(Response, pair, _jwt.CookieDomain);
        return Ok(new AuthResponse(user.ToDto(), pair.AccessToken, pair.AccessExpiresAt));
    }
}
