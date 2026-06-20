using System.Text.Json;
using Cerdik.Application.Ai;
using Cerdik.Domain;

namespace Cerdik.Infrastructure.Ai;

/// <summary>Parses model-based moderation results into <see cref="RiskClassification"/>. Used by the
/// real AI providers; the deterministic <see cref="Heuristics"/> layer remains the fallback so safety
/// never depends solely on a model's output or a successful API call.</summary>
internal static class RiskParsing
{
    /// <summary>Parse the JSON envelope produced by the <c>SystemPrompts.RiskClassifier</c> prompt:
    /// { "risk": "...", "categories": [...], "requires_escalation": bool, "reason": "..." }.</summary>
    public static RiskClassification? ParseClassifierJson(string content)
    {
        var json = ExtractJsonObject(content);
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var risk = root.TryGetProperty("risk", out var r) ? ToRisk(r.GetString()) : RiskLevel.None;
            var categories = root.TryGetProperty("categories", out var c) && c.ValueKind == JsonValueKind.Array
                ? c.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0).ToList()
                : new List<string>();
            var escalate = root.TryGetProperty("requires_escalation", out var e) && e.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? e.GetBoolean()
                : risk >= RiskLevel.High;
            var reason = root.TryGetProperty("reason", out var rs) ? rs.GetString() : null;

            return new RiskClassification(risk, categories, escalate, reason);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Map an OpenAI <c>/v1/moderations</c> result element to a risk classification.</summary>
    public static RiskClassification FromOpenAiModeration(JsonElement result)
    {
        var flagged = result.TryGetProperty("flagged", out var f) && f.ValueKind == JsonValueKind.True;
        var categories = new List<string>();
        var risk = RiskLevel.None;

        if (result.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Object)
        {
            foreach (var cat in cats.EnumerateObject())
            {
                if (cat.Value.ValueKind != JsonValueKind.True) continue;
                var key = cat.Name;
                categories.Add(key);
                risk = (RiskLevel)Math.Max((int)risk, (int)CategoryRisk(key));
            }
        }

        if (flagged && risk == RiskLevel.None) risk = RiskLevel.Medium;
        var escalate = risk >= RiskLevel.High;
        var reason = categories.Count > 0 ? $"Model flagged: {string.Join(", ", categories)}" : null;
        return new RiskClassification(risk, categories.Count > 0 ? categories : new List<string> { "none" }, escalate, reason);
    }

    private static RiskLevel CategoryRisk(string category)
    {
        var c = category.ToLowerInvariant();
        if (c.Contains("self-harm") || c.Contains("self_harm") || c.Contains("sexual/minors")) return RiskLevel.Critical;
        if (c.StartsWith("violence") || c.Contains("threatening") || c == "sexual") return RiskLevel.High;
        if (c is "hate" or "harassment" or "sexual") return RiskLevel.Medium;
        return RiskLevel.Low;
    }

    private static RiskLevel ToRisk(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "critical" => RiskLevel.Critical,
        "high" => RiskLevel.High,
        "medium" => RiskLevel.Medium,
        "low" => RiskLevel.Low,
        _ => RiskLevel.None,
    };

    /// <summary>Pull the first {...} object out of a model response (handles code fences / stray prose).</summary>
    private static string? ExtractJsonObject(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        return start >= 0 && end > start ? content[start..(end + 1)] : null;
    }
}
