using Cerdik.Application.Ai;
using Cerdik.Domain;

namespace Cerdik.Infrastructure.Ai;

/// <summary>Deterministic safety heuristics used by the mock provider and as a defensive
/// fallback layer for the moderation pipeline (so safety never depends solely on an external API).</summary>
internal static class Heuristics
{
    private static readonly string[] CriticalTerms =
        ["kill myself", "suicide", "self harm", "self-harm", "want to die", "end my life", "bunuh diri", "cederakan diri"];

    private static readonly string[] HighTerms =
        ["hurt me", "abuse", "molest", "groom", "kidnap", "weapon", "gun", "bomb", "dera"];

    private static readonly string[] MediumTerms =
        ["address", "phone number", "where do you live", "meet me", "my password", "credit card", "nombor telefon"];

    public static RiskClassification ClassifyRisk(string text)
    {
        var t = (text ?? string.Empty).ToLowerInvariant();

        if (CriticalTerms.Any(t.Contains))
        {
            return new RiskClassification(RiskLevel.Critical, ["self_harm", "distress"], RequiresEscalation: true,
                "Possible self-harm or distress indicator. Escalate to a trusted adult / Safety Reviewer.");
        }
        if (HighTerms.Any(t.Contains))
        {
            return new RiskClassification(RiskLevel.High, ["violence", "harassment"], RequiresEscalation: true,
                "Possible safety risk detected.");
        }
        if (MediumTerms.Any(t.Contains))
        {
            return new RiskClassification(RiskLevel.Medium, ["personal_data"], RequiresEscalation: false,
                "Personal-information sharing detected; redirect away from sharing private details.");
        }
        return new RiskClassification(RiskLevel.None, ["none"], RequiresEscalation: false, null);
    }
}
