namespace Cerdik.Web;

/// <summary>Baseline security response headers for the Blazor Web App, including a Content-Security-Policy
/// tuned for Blazor Server (SignalR websockets + component inline styles).</summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    // Blazor Server needs: 'self' scripts (blazor.web.js), wasm-unsafe-eval, websockets for SignalR,
    // and inline styles (components ship scoped <style> blocks). data: images cover inline avatars.
    private const string Csp =
        "default-src 'self'; " +
        "base-uri 'self'; " +
        "object-src 'none'; " +
        "frame-ancestors 'none'; " +
        "img-src 'self' data:; " +
        "font-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "script-src 'self' 'wasm-unsafe-eval'; " +
        "connect-src 'self' ws: wss:; " +
        "form-action 'self'";

    public Task InvokeAsync(HttpContext context)
    {
        var h = context.Response.Headers;
        h["Content-Security-Policy"] = Csp;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "no-referrer";
        h["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
        h.Remove("Server");
        return _next(context);
    }
}
