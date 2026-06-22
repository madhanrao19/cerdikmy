using System.Net;
using System.Net.Http.Json;
using Cerdik.Application.Dtos;
using FluentAssertions;
using Xunit;

namespace Cerdik.IntegrationTests;

[Collection("api")]
public class AccountLockoutTests
{
    private readonly ApiFactory _factory;

    public AccountLockoutTests(SeededApiFixture fixture) => _factory = fixture.Factory;

    [Fact]
    public async Task Account_locks_after_repeated_failed_logins()
    {
        var client = _factory.CreateClient();
        var email = $"lock.{Guid.NewGuid():N}@cerdik.my";

        var register = await client.PostAsJsonAsync("/auth/register-parent",
            new RegisterParentRequest(email, "Right!Passw0rd", "Lock User", "Lock Household", "BM"));
        register.EnsureSuccessStatusCode();

        // Five wrong attempts trip the lockout.
        for (var i = 0; i < 5; i++)
        {
            var bad = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "wrong-password"));
            bad.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // Now even the CORRECT password is rejected with 429 while the lockout window is active.
        var locked = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "Right!Passw0rd"));
        locked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Successful_login_resets_failure_count()
    {
        var client = _factory.CreateClient();
        var email = $"reset-count.{Guid.NewGuid():N}@cerdik.my";

        var register = await client.PostAsJsonAsync("/auth/register-parent",
            new RegisterParentRequest(email, "Right!Passw0rd", "Reset Count", "RC Household", "BM"));
        register.EnsureSuccessStatusCode();

        // A few failures, then a success, then failures again — the success must have reset the counter
        // so we don't get locked by the carried-over count.
        for (var i = 0; i < 3; i++)
        {
            await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "wrong-password"));
        }
        (await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "Right!Passw0rd")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        for (var i = 0; i < 3; i++)
        {
            (await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "wrong-password")))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized); // still not locked (count was reset)
        }
    }
}
