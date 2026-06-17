using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Cerdik.E2E;

/// <summary>Browser smoke tests against a running stack. Point E2E_BASE_URL at the web app
/// (default http://localhost:5080). Tests self-skip when the app isn't reachable so the suite
/// stays green in environments without a live deployment.
///
/// Prereqs: `docker compose -f infra/docker/docker-compose.yml up -d` then install browsers with
/// `pwsh tests/Cerdik.E2E/bin/Debug/net10.0/playwright.ps1 install`.</summary>
public class SmokeTests : IAsyncLifetime
{
    private static string BaseUrl => Environment.GetEnvironmentVariable("E2E_BASE_URL") ?? "http://localhost:5080";

    private IPlaywright _pw = default!;
    private IBrowser _browser = default!;

    public async Task InitializeAsync()
    {
        _pw = await Playwright.CreateAsync();
        _browser = await _pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _pw?.Dispose();
    }

    private static async Task<bool> AppIsUp()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var resp = await http.GetAsync(BaseUrl);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    [SkippableFact]
    public async Task Home_page_loads_and_shows_cerdik_branding()
    {
        Skip.IfNot(await AppIsUp(), "Web app not reachable; start the stack to run E2E tests.");

        var page = await _browser.NewPageAsync();
        await page.GotoAsync(BaseUrl);
        var content = await page.ContentAsync();
        content.Should().Contain("cerdik", "the landing/login page should show the product name");
    }

    [SkippableFact]
    public async Task Login_page_has_accessible_email_and_password_fields()
    {
        Skip.IfNot(await AppIsUp(), "Web app not reachable; start the stack to run E2E tests.");

        var page = await _browser.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/login");

        (await page.GetByLabel("Email").CountAsync()).Should().BeGreaterThan(0);
        (await page.GetByLabel("Password").CountAsync()).Should().BeGreaterThan(0);
    }
}
