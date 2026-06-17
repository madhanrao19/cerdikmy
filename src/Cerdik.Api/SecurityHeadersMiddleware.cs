namespace Cerdik.Api;

/// <summary>Adds baseline security response headers. The API is JSON-only and sits behind a reverse
/// proxy that terminates TLS; these headers harden it defensively regardless.</summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cross-Origin-Resource-Policy"] = "same-site";
        headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
        // Server header obscured to avoid leaking the stack.
        headers.Remove("Server");
        return _next(context);
    }
}
