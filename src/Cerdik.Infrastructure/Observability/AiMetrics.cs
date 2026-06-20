using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Cerdik.Infrastructure.Observability;

/// <summary>Custom OpenTelemetry instrumentation for the AI tutor + moderation pipeline:
/// a Meter (counts + latency) and an ActivitySource (spans). Registered as a singleton; the meter
/// and source names are wired into the OTel pipeline in the API's Program.cs.</summary>
public sealed class AiMetrics : IDisposable
{
    public const string MeterName = "cerdik.ai";
    public const string ActivitySourceName = "cerdik.ai";

    /// <summary>Span source for tutor generations (e.g. one span per reply).</summary>
    public static readonly ActivitySource Source = new(ActivitySourceName);

    private readonly Meter _meter;
    private readonly Counter<long> _tutorMessages;
    private readonly Histogram<double> _tutorLatencyMs;
    private readonly Counter<long> _moderationEvents;

    public AiMetrics()
    {
        _meter = new Meter(MeterName);
        _tutorMessages = _meter.CreateCounter<long>("cerdik.tutor.messages", "{message}", "Tutor replies generated.");
        _tutorLatencyMs = _meter.CreateHistogram<double>("cerdik.tutor.latency", "ms", "Tutor reply generation latency.");
        _moderationEvents = _meter.CreateCounter<long>("cerdik.moderation.events", "{event}", "Moderation decisions, by stage/decision/risk.");
    }

    public void RecordTutorMessage(double elapsedMs, string provider)
    {
        var tags = new TagList { { "provider", provider } };
        _tutorMessages.Add(1, tags);
        _tutorLatencyMs.Record(elapsedMs, tags);
    }

    public void RecordModeration(string stage, string decision, string risk) =>
        _moderationEvents.Add(1, new TagList { { "stage", stage }, { "decision", decision }, { "risk", risk } });

    public void Dispose() => _meter.Dispose();
}
