using Cerdik.Infrastructure;
using Cerdik.Infrastructure.Jobs;
using Hangfire;
using Hangfire.SqlServer;
using Serilog;

// Dedicated Hangfire worker host. Processes jobs enqueued by the API (content indexing, privacy
// requests) and owns recurring schedules (nightly mastery recompute). Runs the same job code via
// the shared Cerdik.Infrastructure assembly so behaviour is identical to in-API execution.

EnvBootstrap.Load();

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((_, cfg) => cfg.Enrich.FromLogContext().WriteTo.Console());

builder.Configuration.AddEnvironmentVariables();
builder.Services.AddInfrastructure(builder.Configuration);

var conn = builder.Configuration["DATABASE_URL"] ?? builder.Configuration.GetConnectionString("Default");
builder.Services.AddHangfire(cfg => cfg
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(conn, new SqlServerStorageOptions { PrepareSchemaIfNecessary = true }));

builder.Services.AddHangfireServer(options => options.WorkerCount = Environment.ProcessorCount * 2);

var host = builder.Build();

// Recurring jobs owned by the worker.
using (var scope = host.Services.CreateScope())
{
    var recurring = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    recurring.AddOrUpdate<BackgroundJobs>("recompute-mastery", j => j.RecomputeMasteryAsync(), Cron.Daily(20));
}

await host.RunAsync();

/// <summary>Loads a root .env so the worker shares config with the API in local dev.</summary>
internal static class EnvBootstrap
{
    public static void Load()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, ".env");
            if (File.Exists(candidate))
            {
                foreach (var raw in File.ReadAllLines(candidate))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith('#')) continue;
                    var idx = line.IndexOf('=');
                    if (idx <= 0) continue;
                    var key = line[..idx].Trim();
                    var value = line[(idx + 1)..].Trim().Trim('"');
                    if (Environment.GetEnvironmentVariable(key) is null)
                        Environment.SetEnvironmentVariable(key, value);
                }
                return;
            }
            dir = dir.Parent;
        }
    }
}
