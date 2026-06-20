using FluentAssertions;
using Xunit;

namespace Cerdik.IntegrationTests;

[Collection("api")]
public class ObservabilityTests
{
    private readonly ApiFactory _factory;

    public ObservabilityTests(SeededApiFixture fixture) => _factory = fixture.Factory;

    [Fact]
    public async Task Responses_carry_a_correlation_id_header()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/health/live");
        resp.Headers.Contains("X-Correlation-ID").Should().BeTrue();
    }

    [Fact]
    public async Task Inbound_correlation_id_is_echoed_back()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-ID", "test-corr-123");
        var resp = await client.SendAsync(request);
        resp.Headers.GetValues("X-Correlation-ID").Should().Contain("test-corr-123");
    }
}
