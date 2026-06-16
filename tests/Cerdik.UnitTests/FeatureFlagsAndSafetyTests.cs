using Cerdik.Application.Features;
using Cerdik.Domain;
using Cerdik.Infrastructure.Ai;
using FluentAssertions;
using Xunit;

namespace Cerdik.UnitTests;

public class FeatureFlagsTests
{
    [Fact]
    public void Defaults_enable_bm_en_and_gate_zh_ta_dlp()
    {
        var flags = new FeatureFlags();
        flags.LanguageEnabled("bm").Should().BeTrue();
        flags.LanguageEnabled("en").Should().BeTrue();
        flags.LanguageEnabled("zh").Should().BeFalse();
        flags.DlpEnabled("math").Should().BeFalse();
    }

    [Fact]
    public void Overrides_from_env_string_take_effect()
    {
        var flags = new FeatureFlags(FeatureFlags.Parse("lang.zh=true,dlp.science=true"));
        flags.LanguageEnabled("zh").Should().BeTrue();
        flags.DlpEnabled("science").Should().BeTrue();
        flags.DlpEnabled("math").Should().BeFalse();
    }
}

public class HeuristicsAndCosineTests
{
    [Fact]
    public void Risk_classifier_escalates_self_harm_language()
    {
        var result = Heuristics.ClassifyRisk("i want to kill myself");
        result.Risk.Should().Be(RiskLevel.Critical);
        result.RequiresEscalation.Should().BeTrue();
    }

    [Fact]
    public void Risk_classifier_flags_personal_data_sharing()
    {
        var result = Heuristics.ClassifyRisk("here is my phone number 0123");
        result.Risk.Should().Be(RiskLevel.Medium);
    }

    [Fact]
    public void Risk_classifier_allows_normal_question()
    {
        Heuristics.ClassifyRisk("how do I add 3 + 4?").Risk.Should().Be(RiskLevel.None);
    }

    [Fact]
    public void Cosine_of_identical_vectors_is_one()
    {
        var v = new[] { 0.1f, 0.2f, 0.3f, 0.4f };
        VectorRetriever.Cosine(v, v).Should().BeApproximately(1.0, 0.0001);
    }

    [Fact]
    public void Cosine_of_orthogonal_vectors_is_zero()
    {
        VectorRetriever.Cosine([1f, 0f], [0f, 1f]).Should().BeApproximately(0.0, 0.0001);
    }
}
