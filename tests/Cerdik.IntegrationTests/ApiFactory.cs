using Cerdik.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cerdik.IntegrationTests;

/// <summary>Boots the real API in-process against an EF Core InMemory database with the AI provider in
/// mock mode, then seeds the demo dataset. Hangfire/SQL Server are disabled via the Testing environment.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string ParentEmail = "parent.demo@cerdik.my";
    public const string ParentPassword = "Demo!2345";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATABASE_URL"] = "Server=(testing);Database=cerdik;Trusted_Connection=True;", // unused (replaced below)
                ["SEED_ON_STARTUP"] = "false",
                ["AI_PROVIDER"] = "mock",
                ["STORAGE_PROVIDER"] = "s3",
                ["JWT_ACCESS_SECRET"] = "test-access-secret-at-least-32-characters-long!",
                ["JWT_REFRESH_SECRET"] = "test-refresh-secret-at-least-32-characters-long",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Replace the SQL Server DbContext with an InMemory one shared across the host.
            var toRemove = services.Where(d =>
                d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                d.ServiceType == typeof(AppDbContext) ||
                (d.ServiceType.IsGenericType && d.ServiceType.GetGenericTypeDefinition() == typeof(DbContextOptions<>))).ToList();
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
