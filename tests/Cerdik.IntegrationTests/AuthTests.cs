using System.Net;
using System.Net.Http.Json;
using Cerdik.Application.Dtos;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Cerdik.IntegrationTests;

[Collection("api")]
public class AuthTests
{
    private readonly ApiFactory _factory;

    public AuthTests(SeededApiFixture fixture) => _factory = fixture.Factory;

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    [Fact]
    public async Task Register_parent_then_me_returns_profile()
    {
        var client = NewClient();
        var email = $"new.parent.{Guid.NewGuid():N}@cerdik.my";

        var register = await client.PostAsJsonAsync("/auth/register-parent",
            new RegisterParentRequest(email, "Sup3r!Secret", "New Parent", "New Household", "BM"));
        register.StatusCode.Should().Be(HttpStatusCode.OK);

        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>();
        auth!.User.Email.Should().Be(email);
        auth.AccessToken.Should().NotBeNullOrEmpty();

        // The auth cookie set by register should authenticate /me.
        var me = await client.GetFromJsonAsync<MeResponse>("/me");
        me!.User.Email.Should().Be(email);
        me.Features.Should().ContainKey("lang.bm");
    }

    [Fact]
    public async Task Login_with_seeded_parent_succeeds_and_lists_children()
    {
        var client = NewClient();

        var login = await client.PostAsJsonAsync("/auth/login",
            new LoginRequest(ApiFactory.ParentEmail, ApiFactory.ParentPassword));
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var me = await client.GetFromJsonAsync<MeResponse>("/me");
        me!.Students.Should().NotBeEmpty("the seeded parent guardians at least one student");
    }

    [Fact]
    public async Task Login_with_wrong_password_is_unauthorized()
    {
        var client = NewClient();
        var login = await client.PostAsJsonAsync("/auth/login",
            new LoginRequest(ApiFactory.ParentEmail, "wrong-password"));
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_without_auth_is_unauthorized()
    {
        var client = NewClient();
        var resp = await client.GetAsync("/me");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
