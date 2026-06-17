using Cerdik.Domain;

namespace Cerdik.Application.Ai;

/// <summary>A retrieved chunk supplied to the model as grounding context.</summary>
public sealed record RetrievedChunk(
    Guid ChunkId,
    Guid LessonId,
    string LessonTitle,
    string Content,
    double Score);

/// <summary>Conversation turn passed to the provider.</summary>
public sealed record TutorTurn(TutorMessageRole Role, string Content);

/// <summary>Full prompt for a tutor generation, including grounding context and safety metadata.</summary>
public sealed record TutorPrompt
{
    public required string StudentQuestion { get; init; }
    public required Level Level { get; init; }
    public required Language Language { get; init; }
    public string SubjectName { get; init; } = "General";
    public IReadOnlyList<TutorTurn> History { get; init; } = Array.Empty<TutorTurn>();
    public IReadOnlyList<RetrievedChunk> Context { get; init; } = Array.Empty<RetrievedChunk>();
    public string SystemPrompt { get; init; } = string.Empty;
}

/// <summary>Citation emitted by the tutor, mapping back to a grounding chunk.</summary>
public sealed record AiCitation(Guid ChunkId, Guid LessonId, string LessonTitle, string Snippet, double Score);

/// <summary>Structured tutor reply (matches the required answer_markdown/citations/mastery/needs_review shape).</summary>
public sealed record TutorReply
{
    public required string AnswerMarkdown { get; init; }
    public IReadOnlyList<AiCitation> Citations { get; init; } = Array.Empty<AiCitation>();
    public MasteryBand? MasterySignal { get; init; }
    public bool NeedsReview { get; init; }
    public string? Model { get; init; }
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
}

/// <summary>A streamed chunk. <see cref="Final"/> carries the structured payload at end-of-stream.</summary>
public sealed record TutorStreamChunk(string DeltaMarkdown, TutorReply? Final = null)
{
    public bool IsFinal => Final is not null;
}

public sealed record RiskClassification(
    RiskLevel Risk,
    IReadOnlyList<string> Categories,
    bool RequiresEscalation,
    string? Reason);

public sealed record PracticeRequest(
    string SubjectName,
    Level Level,
    Language Language,
    string StandardCode,
    string StandardDescription,
    int Count = 5);

public sealed record PracticeQuestion(
    string Prompt,
    QuestionType Type,
    IReadOnlyList<string> Options,
    string CorrectAnswer,
    string Explanation);

public sealed record PracticeSet(string Title, IReadOnlyList<PracticeQuestion> Questions);

public sealed record ProgressSummaryRequest(
    string StudentName,
    Language Language,
    IReadOnlyList<SubjectProgressInput> Subjects);

public sealed record SubjectProgressInput(string SubjectName, double MasteryScore, MasteryBand Band, int LessonsCompleted);

public sealed record ProgressSummary(string NarrativeMarkdown, IReadOnlyList<string> Recommendations);
