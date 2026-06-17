using Cerdik.Application.Ai;

namespace Cerdik.Application.Abstractions;

/// <summary>Provider-agnostic AI interface. OpenAI / Azure OpenAI / Anthropic adapters all
/// implement this contract; the rest of the app never references a vendor SDK directly.</summary>
public interface IAiProvider
{
    string Name { get; }

    /// <summary>Generate a grounded tutor reply (answer + citations + mastery signal + review flag).</summary>
    Task<TutorReply> GenerateTutorReplyAsync(TutorPrompt prompt, CancellationToken ct = default);

    /// <summary>Stream a tutor reply token-by-token for SSE delivery. The final yielded
    /// <see cref="TutorStreamChunk"/> carries the structured payload (citations, mastery, review).</summary>
    IAsyncEnumerable<TutorStreamChunk> StreamTutorReplyAsync(TutorPrompt prompt, CancellationToken ct = default);

    /// <summary>Classify safety/escalation risk for a piece of text (pre- or post-generation).</summary>
    Task<RiskClassification> ClassifyRiskAsync(string text, CancellationToken ct = default);

    /// <summary>Generate a practice set aligned to a learning standard / subject variant.</summary>
    Task<PracticeSet> GeneratePracticeSetAsync(PracticeRequest request, CancellationToken ct = default);

    /// <summary>Summarize a student's progress into a parent-friendly narrative.</summary>
    Task<ProgressSummary> SummarizeProgressAsync(ProgressSummaryRequest request, CancellationToken ct = default);
}

/// <summary>Embedding generation, separated so retrieval indexing can use a cheaper model.</summary>
public interface IEmbeddingProvider
{
    string Model { get; }
    int Dimensions { get; }
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}

/// <summary>Resolves the configured <see cref="IAiProvider"/> by name (openai/azureopenai/anthropic/mock).</summary>
public interface IAiProviderFactory
{
    IAiProvider Resolve(string? providerName = null);
}
