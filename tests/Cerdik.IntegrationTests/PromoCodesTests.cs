using System.Net;
using System.Net.Http.Json;
using Cerdik.Application.Dtos;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Cerdik.IntegrationTests;

[Collection("api")]
public class PromoCodesTests
{
    private readonly ApiFactory _factory;

    public PromoCodesTests(SeededApiFixture fixture) => _factory = fixture.Factory;

    private async Task<HttpClient> LoginAsync(string email, string password)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var login = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, password));
        login.EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task Admin_creates_and_can_validate_a_promo_code()
    {
        var admin = await LoginAsync("admin@cerdik.my", "Admin!2345");
        var code = "SAVE20-" + Guid.NewGuid().ToString("N")[..6];

        var createResp = await admin.PostAsJsonAsync("/admin/promo-codes",
            new CreatePromoCodeRequest(code, 20, 0, null));
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await createResp.Content.ReadFromJsonAsync<PromoCodeDto>(TestJson.Options);
        dto!.DiscountPercent.Should().Be(20);
        dto.Code.Should().Be(code.ToUpperInvariant());

        var validResp = await admin.PostAsJsonAsync("/billing/promo/validate", new ValidatePromoRequest(code));
        var v = await validResp.Content.ReadFromJsonAsync<PromoValidationDto>(TestJson.Options);
        v!.Valid.Should().BeTrue();
        v.DiscountPercent.Should().Be(20);
    }

    [Fact]
    public async Task Unknown_promo_code_is_invalid()
    {
        var admin = await LoginAsync("admin@cerdik.my", "Admin!2345");
        var resp = await admin.PostAsJsonAsync("/billing/promo/validate",
            new ValidatePromoRequest("NOPE-" + Guid.NewGuid().ToString("N")[..6]));
        var dto = await resp.Content.ReadFromJsonAsync<PromoValidationDto>(TestJson.Options);
        dto!.Valid.Should().BeFalse();
        dto.Reason.Should().Be("invalid");
    }

    [Fact]
    public async Task Expired_promo_code_is_rejected()
    {
        var admin = await LoginAsync("admin@cerdik.my", "Admin!2345");
        var code = "OLD-" + Guid.NewGuid().ToString("N")[..6];
        await admin.PostAsJsonAsync("/admin/promo-codes",
            new CreatePromoCodeRequest(code, 10, 0, DateTimeOffset.UtcNow.AddDays(-1)));

        var resp = await admin.PostAsJsonAsync("/billing/promo/validate", new ValidatePromoRequest(code));
        var dto = await resp.Content.ReadFromJsonAsync<PromoValidationDto>(TestJson.Options);
        dto!.Valid.Should().BeFalse();
        dto.Reason.Should().Be("expired");
    }
}
