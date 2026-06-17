using Cerdik.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cerdik.IntegrationTests;

/// <summary>Boots the real API in-process against an EF Core InMemory database with the AI provider in
/// mock mode, then seeds the demo dataset. Hangfire/SQL Server are disabled via the Testing environment.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string ParentEmail = "parent.demo@cerdik.my";
    public const string ParentPassword = "Demo!2345";

    // Program.cs reads configuration (DATABASE_URL, environment) BEFORE builder.Build(), so the
    // factory's ConfigureAppConfiguration/UseEnvironment (applied at build time) would be too late.
    // Set them as real process env vars here so they're visible when the host's entry point runs.
    static ApiFactory()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("SEED_ON_STARTUP", "false");
        Environment.SetEnvironmentVariable("AI_PROVIDER", "mock");
        Environment.SetEnvironmentVariable("STORAGE_PROVIDER", "s3");
        Environment.SetEnvironmentVariable("DATABASE_URL", "Server=(testing);Database=cerdik;Trusted_Connection=True;");
        Environment.SetEnvironmentVariable("JWT_ACCESS_SECRET", "test-access-secret-at-least-32-characters-long!");
        Environment.SetEnvironmentVariable("JWT_REFRESH_SECRET", "test-refresh-secret-at-least-32-characters-long");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            // Replace the SQL Server DbContext with an InMemory one shared across the host. We strip
            // every DbContextOptions-related registration (incl. EF Core 9/10's
            // IDbContextOptionsConfiguration<AppDbContext>) so the SqlServer provider config can't
            // collide with InMemory ("multiple providers" error).
            var toRemove = services.Where(d =>
                d.ServiceType == typeof(AppDbContext) ||
                (d.ServiceType.FullName?.Contains("DbContextOptions", StringComparison.Ordinal) ?? false)).ToList();
            foreach (var d in toRemove) services.Remove(d);

            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase("cerdik-integration-tests"));
        });
    }

    /// <summary>Idempotently seed the demo dataset into the InMemory store.</summary>
    public async Task SeedAsync()
    {
        using var scope = Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
        await seeder.SeedAsync();
    }

    public AppDbContext NewDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>();
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<SeededApiFixture>;

/// <summary>Shared, seeded factory for the whole integration test collection.</summary>
public sealed class SeededApiFixture : IAsyncLifetime
{
    public ApiFactory Factory { get; } = new();

    public async Task InitializeAsync() => await Factory.SeedAsync();

    public Task DisposeAsync()
    {
        Factory.Dispose();
        return Task.CompletedTask;
    }
}
