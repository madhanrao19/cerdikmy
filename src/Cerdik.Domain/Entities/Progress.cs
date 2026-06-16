using Cerdik.Domain.Common;

namespace Cerdik.Domain.Entities;

/// <summary>A learner's attempt at an activity.</summary>
public class Attempt : BaseEntity
{
    public Guid ActivityId { get; set; }
    public Activity Activity { get; set; } = default!;

    public Guid StudentId { get; set; }
    public Student Student { get; set; } = default!;

    public AttemptStatus Status { get; set; } = AttemptStatus.InProgress;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SubmittedAt { get; set; }

    public int Score { get; set; }
    public int MaxScore { get; set; }
    public double PercentScore { get; set; }
    public bool Passed { get; set; }

    /// <summary>JSON map of questionId -> learner answer.</summary>
    public string AnswersJson { get; set; } = "{}";
    /// <summary>JSON map of questionId -> per-question grading result.</summary>
    public string ResultJson { get; set; } = "{}";
}

/// <summary>Roll-up of a student's progress on a lesson, including KPM-style Tahap Penguasaan.</summary>
public class ProgressRecord : BaseEntity
{
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = default!;

    public Guid LessonId { get; set; }
    public Lesson Lesson { get; set; } = default!;

    public Guid? SubjectId { get; set; }

    public bool Completed { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Continuous mastery score 0–100 (EWMA across attempts + tutor signals).</summary>
    public double MasteryScore { get; set; }

    /// <summary>KPM Tahap Penguasaan band (TP1–TP6) derived from MasteryScore.</summary>
    public MasteryBand TahapPenguasaan { get; set; } = MasteryBand.TP1;

    public int TimeSpentSeconds { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? LastActivityAt { get; set; }
}

/// <summary>A gamification badge awarded to a student.</summary>
public class Badge : BaseEntity
{
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = default!;

    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Icon { get; set; }
    public DateTimeOffset AwardedAt { get; set; } = DateTimeOffset.UtcNow;
}
