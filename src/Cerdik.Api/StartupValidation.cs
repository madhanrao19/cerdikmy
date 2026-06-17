using Cerdik.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Cerdik.Api;

/// <summary>Fail-fast configuration validation run at startup. Catches the most dangerous
/// misconfigurations (weak/empty JWT signing keys) before the app serves traffic, and warns loudly
/// when development defaults are still in use in a Production environment.</summary>
public static class StartupValidation
{
    private const int MinSecretLength = 32; // 256-bit minimum for HS256
    private static readonly string[] DevDefaults =
    [
        "dev-access-secret-change-me-min-32-chars-long-000",
        "dev-refresh-secret-change-me-min-32-chars-long-00",
    ];

    public static void ValidateOrThrow(IServiceProvider services, IHostEnvironment env, ILogger logger)
    {
        var jwt = services.GetRequiredService<IOptions<JwtOptions>>().Value;

        foreach (var (name, secret) in new[] { ("JWT_ACCESS_SECRET", jwt.AccessSecret), ("JWT_REFRESH_SECRET", jwt.RefreshSecret) })
        {
            if (string.IsNullOrWhiteSpace(secret) || secret.Length < MinSecretLength)
            {
                throw new InvalidOperationException(
                    $"{name} must be set and at least {MinSecretLength} characters long. Configure a strong secret before starting.");
            }

            if (env.IsProduction() && DevDefaults.Contains(secret))
            {
                logger.LogWarning(
                    "{Name} is using a built-in development default in a Production environment. Rotate it to a unique secret immediately.", name);
            }
        }
    }
}
