using System.Text.Json;
using Cerdik.Domain;
using Cerdik.Infrastructure.Ai;
using FluentAssertions;
using Xunit;

namespace Cerdik.UnitTests;

public class RiskParsingTests
{
    [Fact]
    public void ParseClassifierJson_reads_risk_and_escalation()
    {
        var json = """{ "risk": "high", "categories": ["self_harm","distress"], "requires_escalation": true, "reason": "concerning" }""";
        var result = RiskParsing.ParseClassifierJson(json);

        result.Should().NotBeNull();
        result!.Risk.Should().Be(RiskLevel.High);
        result.RequiresEscalation.Should().BeTrue();
        result.Categories.Should().Contain("self_harm");
    }

    [Fact]
    public void ParseClassifierJson_handles_code_fences_and_prose()
    {
        var content = "Here you go:\n```json\n{ \"risk\": \"none\", \"categories\": [\"none\"], \"requires_escalation\": false }\n```";
        var result = RiskParsing.ParseClassifierJson(content);

        result.Should().NotBeNull();
        result!.Risk.Should().Be(RiskLevel.None);
    }

    [Fact]
    public void ParseClassifierJson_returns_null_for_garbage()
    {
        RiskParsing.ParseClassifierJson("not json at all").Should().BeNull();
    }

    [Fact]
    public void FromOpenAiModeration_maps_self_harm_to_critical()
    {
        using var doc = JsonDocument.Parse("""
            { "flagged": true, "categories": { "self-harm": true, "violence": false } }
            """);
        var result = RiskParsing.FromOpenAiModeration(doc.RootElement);

        result.Risk.Should().Be(RiskLevel.Critical);
        result.RequiresEscalation.Should().BeTrue();
    }

    [Fact]
    public void FromOpenAiModeration_unflagged_is_none()
    {
        using var doc = JsonDocument.Parse("""{ "flagged": false, "categories": {} }""");
        RiskParsing.FromOpenAiModeration(doc.RootElement).Risk.Should().Be(RiskLevel.None);
    }
}
