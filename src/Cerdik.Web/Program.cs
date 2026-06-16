using System.Net;
using Cerdik.Web.Components;
using Cerdik.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Razor Components with global InteractiveServer (SignalR) render mode.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// API base URL from configuration (default: local API host).
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5081";

// A shared cookie container so the API's httpOnly auth cookies (refresh token)
// persist across requests within a circuit. Each typed client gets its own
// handler instance, but they share the same CookieContainer.
var cookieContainer = new CookieContainer();

builder.Services
    .AddHttpClient<ApiClient>(client => client.BaseAddress = new Uri(apiBaseUrl))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        CookieContainer = cookieContainer,
        UseCookies = true,
    });

builder.Services
    .AddHttpClient<TutorClient>(client =>
    {
        client.BaseAddress = new Uri(apiBaseUrl);
        // SSE streams can run long; disable the per-request timeout.
        client.Timeout = Timeout.InfiniteTimeSpan;
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        CookieContainer = cookieContainer,
        UseCookies = true,
    });

// Scoped application state (per SignalR circuit) and helpers.
builder.Services.AddScoped<AuthStateService>();
builder.Services.AddScoped<CurrentStudentState>();
builder.Services.AddSingleton<MarkdownService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
