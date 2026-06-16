using Cerdik.Domain;

namespace Cerdik.Application.Dtos;

public sealed record CurriculumVersionDto(Guid Id, string Code, string Name, Level Level, int EffectiveYear, bool IsActive);

public sealed record SchoolProfileDto(Guid Id, string Name, SchoolType SchoolType, Language PrimaryLanguage, DlpMode DlpMode);

public sealed record SubjectDto(
    Guid Id,
    Guid CurriculumVersionId,
    string Code,
    string Name,
    string GradeBand,
    Level Level,
    int SortOrder,
    IReadOnlyList<SubjectVariantDto> Variants);

public sealed record SubjectVariantDto(
    Guid Id,
    SchoolType SchoolType,
    Language Language,
    DlpMode DlpMode,
    PublishState State,
    string? Label);

public sealed record LearningStandardDto(
    Guid Id,
    string Code,
    string Strand,
    string Description,
    MasteryBand TargetBand,
    int SortOrder);

/// <summary>Curriculum filter shared by retrieval, lesson listing and the CurriculumFilterBar UI.</summary>
public sealed record CurriculumFilter(
    string? CurriculumVersionCode = null,
    Level? Level = null,
    SchoolType? SchoolType = null,
    Language? Language = null,
    DlpMode? DlpMode = null,
    Guid? SubjectId = null);

// ---- Lessons ----
public sealed record LessonDto(
    Guid Id,
    Guid SubjectVariantId,
    string Slug,
    string Title,
    string Summary,
    int EstimatedMinutes,
    PublishState State,
    IReadOnlyList<LessonBlockDto> Blocks,
    IReadOnlyList<ActivitySummaryDto> Activities);

public sealed record LessonBlockDto(
    Guid Id,
    LessonBlockType Type,
    int SortOrder,
    string? Markdown,
    MediaAssetDto? Media,
    string? ConfigJson);

public sealed record MediaAssetDto(Guid Id, string Url, string ContentType, string? AltText, int? DurationSeconds);

public sealed record ActivitySummaryDto(Guid Id, string Title, ActivityType Type, int MaxScore, int PassThresholdPercent);

public sealed record ActivityDto(
    Guid Id,
    Guid LessonId,
    string Title,
    ActivityType Type,
    int MaxScore,
    int PassThresholdPercent,
    IReadOnlyList<QuestionDto> Questions);

public sealed record QuestionDto(
    string Id,
    string Prompt,
    QuestionType Type,
    IReadOnlyList<string> Options,
    int Points,
    string? Hint)
{
    /// <summary>Correct answer is intentionally omitted from client-facing DTOs;
    /// it lives in <see cref="GradedQuestion"/> server-side only.</summary>
}

public sealed record GradedQuestion(
    string Id,
    string Prompt,
    QuestionType Type,
    IReadOnlyList<string> Options,
    int Points,
    string? Hint,
    string CorrectAnswer,
    string? Explanation);
