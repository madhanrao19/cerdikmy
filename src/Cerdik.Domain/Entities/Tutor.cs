using Cerdik.Domain.Common;

namespace Cerdik.Domain.Entities;

/// <summary>An AI tutor conversation, scoped to a student and curriculum context.</summary>
public class TutorSession : BaseEntity
{
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = default!;

    public Guid? SubjectVariantId { get; set; }
    public SubjectVariant? SubjectVariant { get; set; }

    public Guid? LessonId { get; set; }

    public string Title { get; set; } = "Tutor session";

    // Retrieval context captured at session creation for reproducible RAG.
    public string CurriculumVersionCode { get; set; } = default!;
    public SchoolType SchoolType { get; set; }
    public Language Language { get; set; }
    public DlpMode DlpMode { get; set; }

    public bool NeedsReview { get; set; }
    public RiskLevel HighestRisk { get; set; } = RiskLevel.None;

    public ICollection<TutorMessage> Messages { get; set; } = new List<TutorMessage>();
    public ICollection<ModerationEvent> ModerationEvents { get; set; } = new List<ModerationEvent>();
}

/// <summary>A single message in a tutor session.</summary>
public class TutorMessage : BaseEntity
{
    public Guid TutorSessionId { get; set; }
    public TutorSession TutorSession { get; set; } = default!;

    public TutorMessageRole Role { get; set; }
    public string Content { get; set; } = default!;

    // Assistant-only structured fields:
    public MasteryBand? MasterySignal { get; set; }
    public bool NeedsReview { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public string? ModelUsed { get; set; }

    public ICollection<Citation> Citations { get; set; } = new List<Citation>();
}

/// <summary>A citation linking a tutor answer back to an approved lesson chunk.</summary>
public class Citation : BaseEntity
{
    public Guid TutorMessageId { get; set; }
    public TutorMessage TutorMessage { get; set; } = default!;

    public Guid EmbeddingChunkId { get; set; }
    public EmbeddingChunk EmbeddingChunk { get; set; } = default!;

    public Guid LessonId { get; set; }
    public string LessonTitle { get; set; } = default!;
    public string Snippet { get; set; } = default!;
    public double Score { get; set; }
    public int Ordinal { get; set; }
}

/// <summary>A moderation decision recorded at a pipeline stage.</summary>
public class ModerationEvent : BaseEntity
{
    public Guid TutorSessionId { get; set; }
    public TutorSession TutorSession { get; set; } = default!;

    public Guid? TutorMessageId { get; set; }

    public ModerationStage Stage { get; set; }
    public ModerationDecision Decision { get; set; }
    public RiskLevel Risk { get; set; }
    public string? Categories { get; set; }
    public string? Reason { get; set; }

    /// <summary>True when this event created an intervention/escalation flag for a reviewer.</summary>
    public bool InterventionRaised { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewNotes { get; set; }

    /// <summary>Set once the child's guardians have been emailed about a high-risk flag, so the
    /// notification job doesn't alert them twice.</summary>
    public DateTimeOffset? GuardianNotifiedAt { get; set; }
}

/// <summary>A retrievable, embedded chunk of approved lesson content (RAG corpus).
/// The embedding is stored using SQL Server's native VECTOR type (see EF configuration).</summary>
public class EmbeddingChunk : BaseEntity
{
    public Guid LessonId { get; set; }
    public Lesson Lesson { get; set; } = default!;

    // Denormalized retrieval filters (curriculum_version + school_type + language + dlp_mode + subject).
    public string CurriculumVersionCode { get; set; } = default!;
    public Guid SubjectId { get; set; }
    public SchoolType SchoolType { get; set; }
    public Language Language { get; set; }
    public DlpMode DlpMode { get; set; }

    public int ChunkIndex { get; set; }
    public string Content { get; set; } = default!;
    public int TokenCount { get; set; }

    /// <summary>Embedding vector. Mapped to SQL Server VECTOR(N); the float[] is the canonical form.
    /// A JSON fallback column (EmbeddingJson) is maintained for providers/instances without VECTOR.</summary>
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public string EmbeddingJson { get; set; } = "[]";
    public int Dimensions { get; set; }

    /// <summary>Only chunks from published lessons that passed content review are retrievable.</summary>
    public bool Approved { get; set; }
}
