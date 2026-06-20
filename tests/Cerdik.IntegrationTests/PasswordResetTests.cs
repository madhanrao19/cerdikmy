using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Cerdik.Application.Dtos;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Cerdik.IntegrationTests;

[Collection("api")]
public class PasswordResetTests
{
    private readonly ApiFactory _factory;

    public PasswordResetTests(SeededApiFixture fixture) => _factory = fixture.Factory;

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    [Fact]
    public async Task Full_reset_flow_changes_the_password()
    {
        var client = NewClient();
        var email = $"reset.{Guid.NewGuid():N}@cerdik.my";

        var register = await client.PostAsJsonAsync("/auth/register-parent",
            new RegisterParentRequest(email, "Old!Passw0rd", "Reset User", "Reset Household", "BM"));
        register.EnsureSuccessStatusCode();

        var forgot = await client.PostAsJsonAsync("/auth/forgot-password", new ForgotPasswordRequest(email));
        forgot.StatusCode.Should().Be(HttpStatusCode.OK);

        // Pull the reset token out of the captured email.
        var msg = _factory.Email.Messages.Last(m => m.To == email && m.Subject.Contains("Reset"));
        var token = Regex.Match(msg.Html, @"reset-password\?token=([^""&\s<]+)").Groups[1].Value;
        token.Should().NotBeNullOrEmpty();
        token = Uri.UnescapeDataString(token);

        var reset = await client.PostAsJsonAsync("/auth/reset-password", new ResetPasswordRequest(token, "New!Passw0rd"));
        reset.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Old password no longer works; the new one does.
        var oldLogin = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "Old!Passw0rd"));
        oldLogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var newLogin = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "New!Passw0rd"));
        newLogin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Forgot_password_for_unknown_email_still_returns_ok()
    {
        var client = NewClient();
        var resp = await client.PostAsJsonAsync("/auth/forgot-password",
            new ForgotPasswordRequest($"nobody.{Guid.NewGuid():N}@cerdik.my"));
        resp.StatusCode.Should().Be(HttpStatusCode.OK); // never reveals account existence
    }

    [Fact]
    public async Task Reset_with_invalid_token_is_rejected()
    {
        var client = NewClient();
        var resp = await client.PostAsJsonAsync("/auth/reset-password",
            new ResetPasswordRequest("not-a-real-token", "New!Passw0rd"));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
