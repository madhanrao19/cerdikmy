using Cerdik.Domain;

namespace Cerdik.Application.Dtos;

public sealed record CreateTutorSessionRequest(
    Guid StudentId,
    Guid? SubjectVariantId,
    Guid? LessonId,
    string? Title);

public sealed record TutorSessionDto(
    Guid Id,
    Guid StudentId,
    Guid? SubjectVariantId,
    string Title,
    string CurriculumVersionCode,
    SchoolType SchoolType,
    Language Language,
    DlpMode DlpMode,
    bool NeedsReview,
    RiskLevel HighestRisk,
    IReadOnlyList<TutorMessageDto> Messages);

public sealed record TutorMessageDto(
    Guid Id,
    TutorMessageRole Role,
    string Content,
    MasteryBand? MasterySignal,
    bool NeedsReview,
    IReadOnlyList<CitationDto> Citations,
    DateTimeOffset CreatedAt);

public sealed record CitationDto(Guid ChunkId, Guid LessonId, string LessonTitle, string Snippet, double Score, int Ordinal);

public sealed record SendTutorMessageRequest(string Content);

/// <summary>Non-streaming reply (the SSE endpoint streams the same shape incrementally).</summary>
public sealed record TutorReplyDto(
    Guid MessageId,
    string AnswerMarkdown,
    IReadOnlyList<CitationDto> Citations,
    MasteryBand? MasterySignal,
    bool NeedsReview,
    RiskLevel Risk);
