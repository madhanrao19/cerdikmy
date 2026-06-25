using System.Text;
using System.Text.Json.Serialization;
using Cerdik.Api;
using Cerdik.Api.Auth;
using Cerdik.Api.Health;
using Cerdik.Domain;
using Cerdik.Infrastructure;
using Cerdik.Infrastructure.Jobs;
using Cerdik.Infrastructure.Observability;
using Cerdik.Infrastructure.Persistence;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

EnvLoader.Load();

var builder = WebApplication.CreateBuilder(args);

// Behind a TLS-terminating reverse proxy / tunnel (nginx, Cloudflare Tunnel) the origin is plain
// HTTP. BEHIND_TLS_PROXY=true honours X-Forwarded-* so the real client IP (rate limiting, audit)
// and the original https scheme (Secure cookies, absolute URLs) are recovered at the origin.
var behindProxy = string.Equals(builder.Configuration["BEHIND_TLS_PROXY"], "true", StringComparison.OrdinalIgnoreCase);

// Don't advertise the server stack.
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

// Pull flat env vars (DATABASE_URL, JWT_*, …) into IConfiguration.
builder.Configuration.AddEnvironmentVariables();

// ---- Serilog ----
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// ---- Infrastructure (EF Core, auth, storage, AI, payments, jobs, feature flags) ----
builder.Services.AddInfrastructure(builder.Configuration);

// ---- Controllers + JSON ----
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// ---- Per-IP rate limiting (enforced outside the Testing environment) ----
builder.Services.AddCerdikRateLimiting();

// ---- Auth: JWT bearer read from httpOnly cookie OR Authorization header ----
var jwtAccessSecret = builder.Configuration["JWT_ACCESS_SECRET"] ?? "dev-access-secret-change-me-min-32-chars-long-000";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["JWT_ISSUER"] ?? "cerdikmy",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["JWT_AUDIENCE"] ?? "cerdikmy-clients",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtAccessSecret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                if (string.IsNullOrEmpty(ctx.Token) && ctx.Request.Cookies.TryGetValue(AuthCookies.Access, out var cookie))
                {
                    ctx.Token = cookie;
                }
                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", p => p.RequireRole(nameof(UserRole.Admin), nameof(UserRole.ContentAdmin), nameof(UserRole.SafetyReviewer)));
    options.AddPolicy("ParentOnly", p => p.RequireRole(nameof(UserRole.Parent)));
});

// ---- CORS for the Blazor web app (credentialed) ----
var webOrigin = builder.Configuration["NEXT_PUBLIC_APP_URL"] ?? "http://localhost:5080";
builder.Services.AddCors(o => o.AddPolicy("web", p => p
    .WithOrigins(webOrigin)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

// ---- Hangfire (SQL Server storage). Skipped under the Testing environment. ----
var isTesting = builder.Environment.IsEnvironment("Testing");
var hangfireConn = builder.Configuration["DATABASE_URL"] ?? builder.Configuration.GetConnectionString("Default");
if (!isTesting)
{
    builder.Services.AddHangfire(cfg => cfg
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UseSqlServerStorage(hangfireConn, new SqlServerStorageOptions
        {
            PrepareSchemaIfNecessary = true,
            QueuePollInterval = TimeSpan.FromSeconds(15),
        }));
    builder.Services.AddHangfireServer();
}

// ---- OpenTelemetry (tracing + metrics) ----
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(builder.Configuration["OTEL_SERVICE_NAME"] ?? "cerdikmy-api"))
    .WithTracing(t =>
    {
        t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
        t.AddSource(AiMetrics.ActivitySourceName); // custom tutor spans
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            t.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        }
    })
    .WithMetrics(m =>
    {
        m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
        m.AddMeter(AiMetrics.MeterName); // custom tutor/moderation metrics
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            m.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        }
    });

// ---- Health checks: /health/live (process) + /health/ready (DB) ----
builder.Services.AddHealthChecks()
    .AddCheck<DbHealthCheck>("database", tags: ["ready"]);

var app = builder.Build();

// ---- CLI: `dotnet Cerdik.Api.dll --migrate` / `--seed` for one-off ops in deploy scripts ----
if (args.Contains("--migrate") || args.Contains("--seed"))
{
    await DbInitializer.InitializeAsync(app.Services, seed: args.Contains("--seed"));
    Log.Information("Database init complete (migrate/seed). Exiting.");
    return;
}

// ---- Fail fast on dangerous misconfiguration (weak/empty JWT signing keys) ----
StartupValidation.ValidateOrThrow(app.Services, app.Environment, app.Logger);

// First in the pipeline so correlation/logging, the rate limiter (partitions by client IP) and
// auth all see the real client IP + original https scheme rather than the proxy's.
if (behindProxy)
{
    var forwarded = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        ForwardLimit = 1,
    };
    // The origin is reachable only via the trusted proxy/tunnel, never publicly.
    forwarded.KnownNetworks.Clear();
    forwarded.KnownProxies.Clear();
    app.UseForwardedHeaders(forwarded);
}

app.UseMiddleware<CorrelationMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseCors("web");
if (!isTesting)
{
    app.UseRateLimiter();
}
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (!isTesting)
{
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = [new HangfireDashboardAuthFilter(app.Environment.IsDevelopment())],
    });

    // Recurring jobs.
    RecurringJob.AddOrUpdate<BackgroundJobs>("recompute-mastery", j => j.RecomputeMasteryAsync(), Cron.Daily(20));
    // Email guardians about high-risk safety flags, every 15 minutes (idempotent per flag).
    RecurringJob.AddOrUpdate<BackgroundJobs>("guardian-safety-alerts", j => j.NotifyGuardiansOfFlagsAsync(), "*/15 * * * *");
    // Weekly family learning summary — Mondays at 08:00 (server time).
    RecurringJob.AddOrUpdate<BackgroundJobs>("weekly-parent-digest", j => j.SendWeeklyParentDigestAsync(), Cron.Weekly(DayOfWeek.Monday, 8));
}

app.MapControllers();

// Liveness = process is up (no dependency checks). Readiness = dependencies (DB) are reachable.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
app.MapHealthChecks("/health"); // all checks (back-compat)

// Auto initialize on startup (idempotent). Disable all of it with SEED_ON_STARTUP=false.
// Schema is always ensured; the demo seeder only runs outside Production unless SEED_DEMO_DATA
// forces it (so a Production deploy never gets demo accounts/content or well-known passwords).
// BOOTSTRAP_ADMIN_* (handled inside DbInitializer) creates the real admin when configured.
if (builder.Configuration["SEED_ON_STARTUP"] != "false")
{
    var seedDemoSetting = builder.Configuration["SEED_DEMO_DATA"];
    var seedDemo = string.Equals(seedDemoSetting, "true", StringComparison.OrdinalIgnoreCase)
        || (!app.Environment.IsProduction() && !string.Equals(seedDemoSetting, "false", StringComparison.OrdinalIgnoreCase));

    try
    {
        await DbInitializer.InitializeAsync(app.Services, seed: seedDemo);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Startup DB init failed (the database may not be ready yet).");
    }
}

app.Run();

// Exposed for WebApplicationFactory integration tests.
public partial class Program;
