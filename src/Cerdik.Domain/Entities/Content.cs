using Cerdik.Domain.Common;

namespace Cerdik.Domain.Entities;

/// <summary>An authored lesson scoped to a subject variant. Content is original placeholder material.</summary>
public class Lesson : BaseEntity
{
    public Guid SubjectVariantId { get; set; }
    public SubjectVariant SubjectVariant { get; set; } = default!;

    public Guid? LearningStandardId { get; set; }
    public LearningStandard? LearningStandard { get; set; }

    public string Slug { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string Summary { get; set; } = default!;
    public int EstimatedMinutes { get; set; } = 20;
    public int SortOrder { get; set; }
    public PublishState State { get; set; } = PublishState.Draft;
    public Guid? PublishedByUserId { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }

    public ICollection<LessonBlock> Blocks { get; set; } = new List<LessonBlock>();
    public ICollection<Activity> Activities { get; set; } = new List<Activity>();
    public ICollection<EmbeddingChunk> Chunks { get; set; } = new List<EmbeddingChunk>();
}

/// <summary>An ordered block within a lesson (multimedia-aware).</summary>
public class LessonBlock : BaseEntity
{
    public Guid LessonId { get; set; }
    public Lesson Lesson { get; set; } = default!;

    public LessonBlockType Type { get; set; }
    public int SortOrder { get; set; }

    /// <summary>Markdown body for text/callout/worked-example blocks.</summary>
    public string? Markdown { get; set; }

    public Guid? MediaAssetId { get; set; }
    public MediaAsset? MediaAsset { get; set; }

    /// <summary>JSON for interactive block configuration.</summary>
    public string? ConfigJson { get; set; }
}

/// <summary>A stored media object (image/video/audio) in S3/Azure Blob.</summary>
public class MediaAsset : BaseEntity, ITenantScoped
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = default!;

    public string StorageKey { get; set; } = default!;
    public string FileName { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public long SizeBytes { get; set; }
    public string? AltText { get; set; }
    public int? DurationSeconds { get; set; }
    public Guid UploadedByUserId { get; set; }
}

/// <summary>An activity (quiz/exercise/practice/assessment) attached to a lesson.</summary>
public class Activity : BaseEntity
{
    public Guid LessonId { get; set; }
    public Lesson Lesson { get; set; } = default!;

    public string Title { get; set; } = default!;
    public ActivityType Type { get; set; }
    public int MaxScore { get; set; }
    public int PassThresholdPercent { get; set; } = 50;
    public PublishState State { get; set; } = PublishState.Draft;

    /// <summary>Questions stored as a strongly-shaped JSON payload (see Application.Dtos.QuestionDto).
    /// Kept as JSON so authoring stays flexible without a question-per-row explosion.</summary>
    public string QuestionsJson { get; set; } = "[]";

    public ICollection<Attempt> Attempts { get; set; } = new List<Attempt>();
}
