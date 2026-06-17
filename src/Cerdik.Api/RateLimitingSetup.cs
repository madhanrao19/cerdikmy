using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Cerdik.Api;

/// <summary>Per-IP rate limiting. A generous global limiter protects the whole API; tighter named
/// policies guard brute-force-prone auth and cost-sensitive AI tutor endpoints. Returns 429 with a
/// Retry-After hint. Skipped under the Testing environment so it never makes tests flaky.</summary>
public static class RateLimitingSetup
{
    public const string Auth = "auth";
    public const string Tutor = "tutor";

    public static IServiceCollection AddCerdikRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Global: 120 requests/minute per client IP.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                RateLimitPartition.GetFixedWindowLimiter(ClientKey(http), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 120,
                    Window = TimeSpan.FromMinutes(1),
                }));

            // Auth: 10/minute per IP (login/register/refresh).
            options.AddPolicy(Auth, http =>
                RateLimitPartition.GetFixedWindowLimiter(ClientKey(http), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                }));

            // Tutor: 30/minute per IP (controls AI provider spend / abuse).
            options.AddPolicy(Tutor, http =>
                RateLimitPartition.GetFixedWindowLimiter(ClientKey(http), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30,
                    Window = TimeSpan.FromMinutes(1),
                }));

            options.OnRejected = (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                }
                context.HttpContext.Response.ContentType = "application/json";
                return context.HttpContext.Response.WriteAsync(
                    "{\"error\":\"Too many requests. Please slow down.\",\"code\":\"rate_limited\"}", ct);
            };
        });

        return services;
    }

    private static string ClientKey(HttpContext http) =>
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
