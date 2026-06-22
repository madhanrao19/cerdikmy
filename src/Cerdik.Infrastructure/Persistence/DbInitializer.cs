using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cerdik.Infrastructure.Persistence;

/// <summary>Applies the schema (migrations if present, else EnsureCreated for dev), applies the
/// SQL Server native VECTOR index best-effort, optionally seeds demo data (<paramref name="seed"/>),
/// and always runs the production-safe <see cref="AdminBootstrapper"/> (a no-op unless
/// BOOTSTRAP_ADMIN_* is configured).</summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, bool seed = true, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var log = sp.GetRequiredService<ILogger<AppDbContext>>();

        if (db.Database.IsRelational())
        {
            if (db.Database.GetMigrations().Any())
            {
                log.LogInformation("Applying EF migrations…");
                await db.Database.MigrateAsync(ct);
            }
            else
            {
                log.LogInformation("No migrations found — ensuring schema is created…");
                await db.Database.EnsureCreatedAsync(ct);
            }

            await TryApplyVectorIndexAsync(db, log, ct);
        }
        else
        {
            await db.Database.EnsureCreatedAsync(ct);
        }

        if (seed)
        {
            var seeder = sp.GetRequiredService<DemoDataSeeder>();
            await seeder.SeedAsync(ct);
        }

        // Always safe to run: creates a real admin from BOOTSTRAP_ADMIN_* when configured,
        // and is a no-op otherwise (or when the account already exists).
        var bootstrapper = sp.GetRequiredService<AdminBootstrapper>();
        await bootstrapper.BootstrapAsync(ct);
    }

    /// <summary>Best-effort: add a SQL Server 2025 native VECTOR column + ANN index for fast retrieval.
    /// Silently skipped on engines that don't support VECTOR (the in-process cosine path still works).</summary>
    private static async Task TryApplyVectorIndexAsync(AppDbContext db, ILogger log, CancellationToken ct)
    {
        const string sql = """
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'EmbeddingVector' AND Object_ID = Object_ID(N'EmbeddingChunks'))
            BEGIN
                BEGIN TRY
                    ALTER TABLE [EmbeddingChunks] ADD [EmbeddingVector] VECTOR(384) NULL;
                END TRY
                BEGIN CATCH
                    -- VECTOR type not supported on this SQL Server build; retrieval falls back to JSON cosine.
                END CATCH
            END
            """;
        try
        {
            await db.Database.ExecuteSqlRawAsync(sql, ct);
            log.LogInformation("Vector index check complete.");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Skipping native VECTOR setup (engine may not support it). Using JSON cosine fallback.");
        }
    }
}
