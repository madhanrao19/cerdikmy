namespace Cerdik.Web;

/// <summary>Baseline security response headers for the Blazor Web App, including a Content-Security-Policy
/// tuned for Blazor Server (SignalR websockets + component inline styles) that also permits lesson media
/// served from object storage (presigned S3 / MinIO / Azure Blob URLs).</summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _csp;

    public SecurityHeadersMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;

        // Lesson media renders directly from presigned object-storage URLs. `https:` covers prod
        // S3 / Azure Blob; add extra origins (e.g. a dev MinIO http://localhost:9000) via the
        // space-separated MEDIA_CSP_ORIGINS / ContentSecurity:MediaOrigins setting.
        var extra = config["MEDIA_CSP_ORIGINS"] ?? config["ContentSecurity:MediaOrigins"] ?? "";
        var media = string.IsNullOrWhiteSpace(extra) ? "https:" : $"https: {extra.Trim()}";

        // Blazor Server needs: 'self' scripts (blazor.web.js) + wasm-unsafe-eval, websockets for SignalR,
        // and inline styles (components ship scoped <style> blocks). data: images cover inline avatars.
        _csp = string.Join("; ",
            "default-src 'self'",
            "base-uri 'self'",
            "object-src 'none'",
            "frame-ancestors 'none'",
            $"img-src 'self' data: {media}",
            $"media-src 'self' {media}",
            $"frame-src 'self' {media}",
            "font-src 'self'",
            "style-src 'self' 'unsafe-inline'",
            "script-src 'self' 'wasm-unsafe-eval'",
            "connect-src 'self' ws: wss:",
            "form-action 'self'");
    }

    public Task InvokeAsync(HttpContext context)
    {
        var h = context.Response.Headers;
        h["Content-Security-Policy"] = _csp;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "no-referrer";
        h["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
        h.Remove("Server");
        return _next(context);
    }
}
