using System.Security.Cryptography;
using Cerdik.Api.Auth;
using Cerdik.Application.Abstractions;
using Cerdik.Application.Dtos;
using Cerdik.Application.Email;
using Cerdik.Domain;
using Cerdik.Domain.Entities;
using Cerdik.Infrastructure.Options;
using Cerdik.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cerdik.Api.Controllers;

[ApiController]
[Route("auth")]
[EnableRateLimiting(RateLimitingSetup.Auth)]
public sealed class AuthController : ControllerBase
{
    private const int ResetTokenTtlMinutes = 30;
    private const int MaxFailedLogins = 5;
    private static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(15);

    private readonly AppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly JwtOptions _jwt;
    private readonly IClock _clock;
    private readonly ICurrentUser _current;
    private readonly IEmailSender _email;
    private readonly IConfiguration _config;

    public AuthController(AppDbContext db, IPasswordHasher hasher, ITokenService tokens, IOptions<JwtOptions> jwt,
        IClock clock, ICurrentUser current, IEmailSender email, IConfiguration config)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _jwt = jwt.Value;
        _clock = clock;
        _current = current;
        _email = email;
        _config = config;
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

        // Best-effort welcome email (SmtpEmailSender never throws into the request).
        var welcome = EmailTemplates.Welcome(user.FullName ?? "there");
        await _email.SendAsync(user.Email, welcome.Subject, welcome.Html, ct);

        return await IssueAsync(user, ct);
    }

    /// <summary>Always returns 200 (never reveals whether an account exists). When the email maps to an
    /// active account, a single-use, time-limited reset link is emailed.</summary>
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email && u.IsActive, ct);
        if (user is not null)
        {
            var prior = await _db.PasswordResetTokens.Where(t => t.UserId == user.Id && t.UsedAt == null).ToListAsync(ct);
            foreach (var t in prior) t.UsedAt = _clock.UtcNow;

            var raw = GenerateOpaqueToken();
            _db.PasswordResetTokens.Add(new PasswordResetToken
            {
                UserId = user.Id,
                TokenHash = _tokens.HashRefreshToken(raw),
                ExpiresAt = _clock.UtcNow.AddMinutes(ResetTokenTtlMinutes),
                RequestedFromIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            });
            await _db.SaveChangesAsync(ct);

            var appUrl = (_config["NEXT_PUBLIC_APP_URL"] ?? "http://localhost:5080").TrimEnd('/');
            var resetUrl = $"{appUrl}/reset-password?token={Uri.EscapeDataString(raw)}";
            var (subject, html) = EmailTemplates.PasswordReset(resetUrl, ResetTokenTtlMinutes);
            await _email.SendAsync(user.Email, subject, html, ct);
        }
        return Ok(new { status = "ok" });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req, CancellationToken ct)
    {
        Validate.Password(req.NewPassword);
        if (string.IsNullOrWhiteSpace(req.Token))
        {
            throw ApiException.BadRequest("Reset token is required.", "invalid_token");
        }

        var hash = _tokens.HashRefreshToken(req.Token);
        var token = await _db.PasswordResetTokens.Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null || token.User is null || token.UsedAt != null || token.ExpiresAt < _clock.UtcNow)
        {
            throw ApiException.BadRequest("This reset link is invalid or has expired.", "invalid_token");
        }

        token.User.PasswordHash = _hasher.Hash(req.NewPassword);
        token.UsedAt = _clock.UtcNow;

        // Revoke all refresh tokens so existing sessions can't continue with the old credentials.
        var refresh = await _db.RefreshTokens.Where(t => t.UserId == token.UserId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in refresh) t.RevokedAt = _clock.UtcNow;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static string GenerateOpaqueToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(48);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    [HttpPost("register-student")]
    [Authorize(Roles = "Parent,Admin")]
    public async Task<ActionResult<AuthResponse>> RegisterStudent([FromBody] RegisterStudentRequest req, CancellationToken ct)
    {
        var household = await _db.Households.Include(h => h.Organization).FirstOrDefaultAsync(h => h.Id == req.HouseholdId, ct)
            ?? throw ApiException.NotFound("Household");

        // A parent may only add students to their OWN household; admins may target any household.
        if (!_current.IsInRole(UserRole.Admin) &&
            !await _db.Users.AnyAsync(u => u.Id == _current.UserId && u.HouseholdId == household.Id, ct))
        {
            throw ApiException.Forbidden("You can only add students to your own household.");
        }

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
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email, ct);
        if (user is null || !user.IsActive)
        {
            throw ApiException.Unauthorized("Invalid email or password.");
        }

        // Account lockout: block while a lockout window is active.
        if (user.LockoutEndsAt is { } until && until > _clock.UtcNow)
        {
            throw new ApiException(StatusCodes.Status429TooManyRequests,
                "Too many failed attempts. This account is temporarily locked — try again later.", "account_locked");
        }

        if (!_hasher.Verify(req.Password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedLogins)
            {
                user.LockoutEndsAt = _clock.UtcNow.Add(LockoutWindow);
                user.FailedLoginCount = 0;
            }
            await _db.SaveChangesAsync(ct);
            throw ApiException.Unauthorized("Invalid email or password.");
        }

        // Success — clear any failure state.
        user.FailedLoginCount = 0;
        user.LockoutEndsAt = null;
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
        AuthCookies.Clear(Response, _jwt.CookieDomain);
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
