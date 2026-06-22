using Cerdik.Application.Abstractions;
using Cerdik.Domain;
using Cerdik.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cerdik.Infrastructure.Persistence;

/// <summary>Creates the first real administrator on an otherwise empty deployment from
/// <c>BOOTSTRAP_ADMIN_EMAIL</c> / <c>BOOTSTRAP_ADMIN_PASSWORD</c> (optionally
/// <c>BOOTSTRAP_ADMIN_NAME</c>). Unlike the demo seeder, this is safe to run in Production: it never
/// uses a hard-coded password, is idempotent (skips if the email already exists), and reuses an
/// existing organization when one is present. Does nothing when the env vars are unset.</summary>
public sealed class AdminBootstrapper
{
    private const int MinPasswordLength = 8;

    private readonly AppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IConfiguration _config;
    private readonly ILogger<AdminBootstrapper> _log;

    public AdminBootstrapper(AppDbContext db, IPasswordHasher hasher, IConfiguration config, ILogger<AdminBootstrapper> log)
    {
        _db = db;
        _hasher = hasher;
        _config = config;
        _log = log;
    }

    public async Task BootstrapAsync(CancellationToken ct = default)
    {
        var email = _config["BOOTSTRAP_ADMIN_EMAIL"]?.Trim();
        var password = _config["BOOTSTRAP_ADMIN_PASSWORD"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return; // Not configured — nothing to do.
        }

        if (password.Length < MinPasswordLength)
        {
            _log.LogError(
                "BOOTSTRAP_ADMIN_PASSWORD is too short ({Length} chars, need at least {Min}). Skipping admin bootstrap.",
                password.Length, MinPasswordLength);
            return;
        }

        var normalizedEmail = email.ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email.ToLower() == normalizedEmail, ct))
        {
            _log.LogInformation("Admin bootstrap skipped — a user with {Email} already exists.", email);
            return;
        }

        var org = await _db.Organizations.OrderBy(o => o.Id).FirstOrDefaultAsync(ct);
        if (org is null)
        {
            org = new Organization { Name = "cerdikMY", Slug = "cerdikmy" };
            _db.Organizations.Add(org);
        }

        _db.Users.Add(new User
        {
            Organization = org,
            Email = email,
            FullName = _config["BOOTSTRAP_ADMIN_NAME"]?.Trim() is { Length: > 0 } name ? name : "Platform Admin",
            Role = UserRole.Admin,
            EmailConfirmed = true,
            PasswordHash = _hasher.Hash(password),
        });

        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Bootstrapped administrator account {Email}.", email);
    }
}
