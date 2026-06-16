using Cerdik.Domain;
using Cerdik.Infrastructure.Jobs;
using FluentAssertions;
using Xunit;

namespace Cerdik.UnitTests;

public class MasteryMathTests
{
    [Theory]
    [InlineData(0, MasteryBand.TP1)]
    [InlineData(24, MasteryBand.TP1)]
    [InlineData(25, MasteryBand.TP2)]
    [InlineData(45, MasteryBand.TP3)]
    [InlineData(60, MasteryBand.TP4)]
    [InlineData(75, MasteryBand.TP5)]
    [InlineData(90, MasteryBand.TP6)]
    [InlineData(100, MasteryBand.TP6)]
    public void ToBand_maps_score_to_tahap_penguasaan(double score, MasteryBand expected) =>
        MasteryMath.ToBand(score).Should().Be(expected);

    [Fact]
    public void UpdateScore_uses_first_attempt_directly()
    {
        MasteryMath.UpdateScore(0, 80).Should().Be(80);
    }

    [Fact]
    public void UpdateScore_applies_ewma_to_subsequent_attempts()
    {
        // current=80, attempt=100, alpha=0.4 => 0.4*100 + 0.6*80 = 88
        MasteryMath.UpdateScore(80, 100).Should().BeApproximately(88, 0.001);
    }
}
