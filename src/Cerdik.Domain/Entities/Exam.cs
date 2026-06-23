using Cerdik.Domain.Common;

namespace Cerdik.Domain.Entities;

/// <summary>A timed mock-exam attempt for a subject. The assembled questions (with answers) are
/// snapshotted at start so grading is stable, and a per-standard breakdown is stored for analytics.</summary>
public class ExamAttempt : BaseEntity
{
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = default!;

    public Guid SubjectId { get; set; }
    public string SubjectName { get; set; } = default!;

    public int QuestionCount { get; set; }
    public int CorrectCount { get; set; }
    public double PercentScore { get; set; }
    public MasteryBand Band { get; set; } = MasteryBand.TP1;

    /// <summary>Malaysian-style letter grade derived from the percentage (A/B/C/D/E/G).</summary>
    public string Grade { get; set; } = "—";

    public int DurationSeconds { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SubmittedAt { get; set; }

    /// <summary>Assembled questions WITH correct answers (server-only) — graded against at submit.</summary>
    public string QuestionsJson { get; set; } = "[]";

    /// <summary>Per-standard result snapshot (JSON) for analytics.</summary>
    public string StandardsJson { get; set; } = "[]";
}
