using Cerdik.Domain;
using Cerdik.Infrastructure.Auth;
using Cerdik.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cerdik.IntegrationTests;

/// <summary>Unit-level coverage for the production-safe <see cref="AdminBootstrapper"/>: it creates a
/// real admin only when configured, never duplicates one, and rejects weak passwords.</summary>
public sealed class AdminBootstrapperTests
{
    private static (AppDbContext Db, AdminBootstrapper Bootstrapper) Build(
        Dictionary<string, string?> settings, string dbName)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var bootstrapper = new AdminBootstrapper(
            db, new BcryptPasswordHasher(), config, NullLogger<AdminBootstrapper>.Instance);
        return (db, bootstrapper);
    }

    [Fact]
    public async Task Does_nothing_when_not_configured()
    {
        var (db, bootstrapper) = Build(new(), nameof(Does_nothing_when_not_configured));

        await bootstrapper.BootstrapAsync();

        (await db.Users.AnyAsync()).Should().BeFalse();
        (await db.Organizations.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Creates_admin_and_org_on_empty_database()
    {
        var (db, bootstrapper) = Build(new()
        {
            ["BOOTSTRAP_ADMIN_EMAIL"] = "owner@cerdik.my",
            ["BOOTSTRAP_ADMIN_PASSWORD"] = "Str0ng-Pass!",
            ["BOOTSTRAP_ADMIN_NAME"] = "Owner",
        }, nameof(Creates_admin_and_org_on_empty_database));

        await bootstrapper.BootstrapAsync();

        var admin = await db.Users.SingleAsync();
        admin.Email.Should().Be("owner@cerdik.my");
        admin.FullName.Should().Be("Owner");
        admin.Role.Should().Be(UserRole.Admin);
        admin.EmailConfirmed.Should().BeTrue();
        admin.PasswordHash.Should().NotBeNullOrWhiteSpace();
        admin.PasswordHash.Should().NotBe("Str0ng-Pass!"); // hashed, not stored in clear
        (await db.Organizations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Is_idempotent_when_admin_already_exists()
    {
        var settings = new Dictionary<string, string?>
        {
            ["BOOTSTRAP_ADMIN_EMAIL"] = "owner@cerdik.my",
            ["BOOTSTRAP_ADMIN_PASSWORD"] = "Str0ng-Pass!",
        };
        const string dbName = nameof(Is_idempotent_when_admin_already_exists);

        var (db1, first) = Build(settings, dbName);
        await first.BootstrapAsync();

        var (db2, second) = Build(settings, dbName); // same InMemory store
        await second.BootstrapAsync();

        (await db2.Users.CountAsync(u => u.Email == "owner@cerdik.my")).Should().Be(1);
    }

    [Fact]
    public async Task Skips_when_password_too_short()
    {
        var (db, bootstrapper) = Build(new()
        {
            ["BOOTSTRAP_ADMIN_EMAIL"] = "owner@cerdik.my",
            ["BOOTSTRAP_ADMIN_PASSWORD"] = "short",
        }, nameof(Skips_when_password_too_short));

        await bootstrapper.BootstrapAsync();

        (await db.Users.AnyAsync()).Should().BeFalse();
    }
}
