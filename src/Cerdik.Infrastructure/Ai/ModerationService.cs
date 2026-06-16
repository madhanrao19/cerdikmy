using Cerdik.Application.Abstractions;
using Cerdik.Domain;

namespace Cerdik.Infrastructure.Ai;

/// <summary>Two-stage moderation. Combines the configured AI provider's risk classifier with the
/// deterministic <see cref="Heuristics"/> safety net (taking the higher risk of the two), then maps
/// risk to an allow/flag/block/escalate decision and decides whether to raise a human intervention.</summary>
public sealed class ModerationService : IModerationService
{
    private readonly IAiProviderFactory _factory;

    public ModerationService(IAiProviderFactory factory) => _factory = factory;

    public async Task<ModerationOutcome> ScreenAsync(string text, ModerationStage stage, CancellationToken ct = default)
    {
        var provider = _factory.Resolve();

        var heuristic = Heuristics.ClassifyRisk(text);
        RiskClassification ai;
        try
        {
            ai = await provider.ClassifyRiskAsync(text, ct);
        }
        catch
        {
            ai = heuristic; // never let a provider outage weaken safety
        }

        // Take the stricter of the two signals.
        var risk = (RiskLevel)Math.Max((int)heuristic.Risk, (int)ai.Risk);
        var escalate = heuristic.RequiresEscalation || ai.RequiresEscalation;
        var categories = heuristic.Categories.Concat(ai.Categories).Distinct().ToList();
        var reason = ai.Reason ?? heuristic.Reason;

        var decision = risk switch
        {
            RiskLevel.Critical => ModerationDecision.Escalate,
            RiskLevel.High => ModerationDecision.Block,
            RiskLevel.Medium => ModerationDecision.Flag,
            _ => ModerationDecision.Allow,
        };

        // Raise an intervention for anything that needs an adult to look at it.
        var raise = escalate || risk >= RiskLevel.High;

        return new ModerationOutcome(decision, risk, categories, raise, reason);
    }
}
