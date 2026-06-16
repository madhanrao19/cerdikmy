namespace Cerdik.Application.Features;

/// <summary>Simple, dependency-free feature-flag system for staged rollouts of languages and DLP variants.
/// Defaults live in code; overrides come from configuration / the FEATURE_FLAGS env var
/// ("lang.zh=true,dlp.math=false"). Designed to be replaced by a remote flag service later without
/// touching call sites.</summary>
public interface IFeatureFlags
{
    bool IsEnabled(string key);
    bool LanguageEnabled(string languageCode);
    bool DlpEnabled(string subjectKey);
    IReadOnlyDictionary<string, bool> Snapshot();
}

public sealed class FeatureFlags : IFeatureFlags
{
    /// <summary>Conservative defaults. BM/EN ship on; ZH/TA and DLP are gated for staged rollout.</summary>
    public static readonly IReadOnlyDictionary<string, bool> Defaults = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
    {
        ["lang.bm"] = true,
        ["lang.en"] = true,
        ["lang.zh"] = false,
        ["lang.ta"] = false,
        ["dlp.science"] = false,
        ["dlp.math"] = false,
        ["billing.curlec"] = true,
        ["tutor.streaming"] = true,
    };

    private readonly Dictionary<string, bool> _flags;

    public FeatureFlags(IEnumerable<KeyValuePair<string, bool>>? overrides = null)
    {
        _flags = new Dictionary<string, bool>(Defaults, StringComparer.OrdinalIgnoreCase);
        if (overrides is null) return;
        foreach (var kv in overrides)
        {
            _flags[kv.Key] = kv.Value;
        }
    }

    /// <summary>Parse a "key=bool,key=bool" string (e.g. the FEATURE_FLAGS env var).</summary>
    public static IEnumerable<KeyValuePair<string, bool>> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) yield break;
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0) continue;
            var key = part[..idx].Trim();
            var val = part[(idx + 1)..].Trim();
            if (bool.TryParse(val, out var b))
            {
                yield return new KeyValuePair<string, bool>(key, b);
            }
        }
    }

    public bool IsEnabled(string key) => _flags.TryGetValue(key, out var v) && v;

    public bool LanguageEnabled(string languageCode) => IsEnabled($"lang.{languageCode.ToLowerInvariant()}");

    public bool DlpEnabled(string subjectKey) => IsEnabled($"dlp.{subjectKey.ToLowerInvariant()}");

    public IReadOnlyDictionary<string, bool> Snapshot() => new Dictionary<string, bool>(_flags);
}
