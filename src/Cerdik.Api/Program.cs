using System.Text;
using System.Text.Json.Serialization;
using Cerdik.Api;
using Cerdik.Api.Auth;
using Cerdik.Api.Health;
using Cerdik.Domain;
using Cerdik.Infrastructure;
using Cerdik.Infrastructure.Jobs;
using Cerdik.Infrastructure.Persistence;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

EnvLoader.Load();

var builder = WebApplication.CreateBuilder(args);

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

// ---- OpenTelemetry (tracing) ----
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(builder.Configuration["OTEL_SERVICE_NAME"] ?? "cerdikmy-api"))
    .WithTracing(t =>
    {
        t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            t.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
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
}

app.MapControllers();

// Liveness = process is up (no dependency checks). Readiness = dependencies (DB) are reachable.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
app.MapHealthChecks("/health"); // all checks (back-compat)

// Auto initialize + seed on startup (idempotent). Disable with SEED_ON_STARTUP=false.
if (builder.Configuration["SEED_ON_STARTUP"] != "false")
{
    try
    {
        await DbInitializer.InitializeAsync(app.Services, seed: true);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Startup DB init failed (the database may not be ready yet).");
    }
}

app.Run();

// Exposed for WebApplicationFactory integration tests.
public partial class Program;
