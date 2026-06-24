using Cerdik.Web;
using Cerdik.Web.Components;
using Cerdik.Web.Services;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// When the app runs behind a TLS-terminating reverse proxy / tunnel (nginx, Cloudflare Tunnel),
// the origin is reached over plain HTTP. BEHIND_TLS_PROXY=true makes the app honour the
// X-Forwarded-* headers (so it knows the real client IP + the original https scheme) and skips
// origin-side HTTPS redirection/HSTS (TLS is enforced at the edge — redirecting would loop).
var behindProxy = string.Equals(builder.Configuration["BEHIND_TLS_PROXY"], "true", StringComparison.OrdinalIgnoreCase);

// Razor Components with global InteractiveServer (SignalR) render mode.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// API base URL. The container/compose sets API_BASE_URL (e.g. http://api:8080); local dev uses the
// ApiBaseUrl appsettings key (http://localhost:5081). Env wins so the same image works in both.
var apiBaseUrl = builder.Configuration["API_BASE_URL"]
    ?? builder.Configuration["ApiBaseUrl"]
    ?? "http://localhost:5081";

// The web app authenticates the API with a per-circuit bearer token (held in the scoped
// AccessTokenProvider) rather than a shared cookie jar — a shared CookieContainer would leak one
// user's session to another. Cookies are disabled on the handlers.
builder.Services
    .AddHttpClient<ApiClient>(client => client.BaseAddress = new Uri(apiBaseUrl))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = false });

builder.Services
    .AddHttpClient<TutorClient>(client =>
    {
        client.BaseAddress = new Uri(apiBaseUrl);
        // SSE streams can run long; disable the per-request timeout.
        client.Timeout = Timeout.InfiniteTimeSpan;
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = false });

// Scoped application state (per SignalR circuit) and helpers.
builder.Services.AddScoped<AccessTokenProvider>();
builder.Services.AddScoped<AuthStateService>();
builder.Services.AddScoped<CurrentStudentState>();
builder.Services.AddSingleton<MarkdownService>();

// Localization (BM / EN / ZH / TA). Culture comes from the .AspNetCore.Culture cookie set by the
// LanguageSwitcher; English is the fallback.
builder.Services.AddLocalization();
builder.Services.AddScoped<IUiText, UiText>();

var app = builder.Build();

// Must run first so every downstream component (logging, auth, the strict CSP) sees the real
// client IP and the original https scheme.
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

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    if (!behindProxy) app.UseHsts(); // HSTS is set at the edge when behind a TLS proxy.
}

app.UseRequestLocalization(new Microsoft.AspNetCore.Builder.RequestLocalizationOptions()
    .SetDefaultCulture("en")
    .AddSupportedCultures(UiText.Supported)
    .AddSupportedUICultures(UiText.Supported));

app.UseMiddleware<SecurityHeadersMiddleware>();
if (!behindProxy) app.UseHttpsRedirection(); // the edge serves HTTPS; the origin is plain HTTP.
app.UseAntiforgery();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
