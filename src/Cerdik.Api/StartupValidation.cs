using Cerdik.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Cerdik.Api;

/// <summary>Fail-fast configuration validation run at startup. Catches the most dangerous
/// misconfigurations before the app serves traffic. In any environment it rejects weak/empty JWT
/// signing keys; in <b>Production</b> it additionally refuses to start when built-in development
/// default credentials (JWT secrets, the demo SQL Server <c>sa</c> password, or the MinIO
/// <c>minioadmin</c> keys) are still in use — a known-credentials production deploy is treated as a
/// hard error, not a warning. The local prod-parity stack opts out explicitly via
/// <c>ALLOW_DEV_DEFAULT_SECRETS=true</c> (set in <c>.env.example</c>), which downgrades these hard
/// failures to warnings.</summary>
public static class StartupValidation
{
    private const int MinSecretLength = 32; // 256-bit minimum for HS256

    private static readonly string[] DevJwtSecrets =
    [
        "dev-access-secret-change-me-min-32-chars-long-000",
        "dev-refresh-secret-change-me-min-32-chars-long-00",
    ];

    // Sentinels shipped in .env.example / docker-compose defaults. Safe for local dev, never prod.
    private const string DevSaPassword = "Cerdik!Passw0rd";
    private const string DevMinioCredential = "minioadmin";

    public static void ValidateOrThrow(IServiceProvider services, IHostEnvironment env, ILogger logger)
    {
        var jwt = services.GetRequiredService<IOptions<JwtOptions>>().Value;
        var storage = services.GetRequiredService<IOptions<StorageOptions>>().Value;
        var config = services.GetRequiredService<IConfiguration>();
        var isProd = env.IsProduction();

        // Explicit local-only opt-out so the prod-parity compose stack can boot with dev defaults.
        var allowDevDefaults = string.Equals(config["ALLOW_DEV_DEFAULT_SECRETS"], "true", StringComparison.OrdinalIgnoreCase);
        var enforce = isProd && !allowDevDefaults;
        // Only surface dev-default warnings in Production (parity mode); they're expected in dev.
        var warn = isProd && allowDevDefaults;

        if (isProd && allowDevDefaults)
        {
            logger.LogWarning(
                "ALLOW_DEV_DEFAULT_SECRETS=true in a Production environment — development default credentials are " +
                "permitted. This is intended for local prod-parity only; never set it on a real deployment.");
        }

        // JWT signing keys — required everywhere, must be strong, must not be dev defaults in prod.
        foreach (var (name, secret) in new[] { ("JWT_ACCESS_SECRET", jwt.AccessSecret), ("JWT_REFRESH_SECRET", jwt.RefreshSecret) })
        {
            if (string.IsNullOrWhiteSpace(secret) || secret.Length < MinSecretLength)
            {
                throw new InvalidOperationException(
                    $"{name} must be set and at least {MinSecretLength} characters long. Configure a strong secret before starting.");
            }

            RejectDevDefault(enforce, warn, logger, name, secret, DevJwtSecrets);
        }

        // SQL Server sa password embedded in the connection string.
        var dbUrl = config["DATABASE_URL"] ?? config.GetConnectionString("Default");
        if (!string.IsNullOrEmpty(dbUrl) && dbUrl.Contains(DevSaPassword, StringComparison.Ordinal))
        {
            const string msg = "DATABASE_URL still uses the built-in development SQL Server password. " +
                "Set a strong MSSQL_SA_PASSWORD (see scripts/generate-secrets.sh) before going to production.";
            if (enforce) throw new InvalidOperationException(msg);
            if (warn) logger.LogWarning(msg);
        }

        // Object storage credentials (only when the S3/MinIO provider is active).
        if (storage.Provider.Equals("s3", StringComparison.OrdinalIgnoreCase))
        {
            RejectDevDefault(enforce, warn, logger, "S3_ACCESS_KEY", storage.S3AccessKey, [DevMinioCredential]);
            RejectDevDefault(enforce, warn, logger, "S3_SECRET_KEY", storage.S3SecretKey, [DevMinioCredential]);
        }

        logger.LogInformation("Startup configuration validation passed ({Environment}).", env.EnvironmentName);
    }

    private static void RejectDevDefault(bool enforce, bool warn, ILogger logger, string name, string value, string[] devDefaults)
    {
        if (!devDefaults.Contains(value))
        {
            return;
        }

        var msg = $"{name} is using a built-in development default credential. " +
            "Rotate it to a unique secret (see scripts/generate-secrets.sh) before going to production.";
        if (enforce)
        {
            throw new InvalidOperationException(msg);
        }

        if (warn)
        {
            logger.LogWarning(msg);
        }
    }
}
