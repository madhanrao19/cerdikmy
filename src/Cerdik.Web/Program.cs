using Cerdik.Web;
using Cerdik.Web.Components;
using Cerdik.Web.Services;

var builder = WebApplication.CreateBuilder(args);

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

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
