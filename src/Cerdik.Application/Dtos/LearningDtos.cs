using Cerdik.Domain;

namespace Cerdik.Application.Dtos;

// ---- Attempts ----
public sealed record StartActivityRequest(Guid StudentId);
public sealed record AttemptDto(Guid Id, Guid ActivityId, Guid StudentId, AttemptStatus Status, int Score, int MaxScore, double PercentScore, bool Passed, DateTimeOffset StartedAt, DateTimeOffset? SubmittedAt);

public sealed record SubmitAttemptRequest(IReadOnlyDictionary<string, string> Answers);

public sealed record AttemptResultDto(
    Guid AttemptId,
    int Score,
    int MaxScore,
    double PercentScore,
    bool Passed,
    MasteryBand TahapPenguasaan,
    IReadOnlyList<QuestionResultDto> Questions);

public sealed record QuestionResultDto(string QuestionId, bool Correct, string Given, string Correct_Answer, string? Explanation);

// ---- Progress ----
public sealed record ProgressDto(
    Guid StudentId,
    string StudentName,
    double OverallMastery,
    MasteryBand OverallBand,
    int LessonsCompleted,
    int TotalLessons,
    IReadOnlyList<SubjectProgressDto> Subjects,
    IReadOnlyList<ProgressHeatCell> Heatmap,
    IReadOnlyList<BadgeDto> Badges);

public sealed record SubjectProgressDto(
    Guid SubjectId,
    string SubjectName,
    double MasteryScore,
    MasteryBand Band,
    int LessonsCompleted,
    int TotalLessons,
    DateTimeOffset? LastActivityAt);

/// <summary>One day in the activity heatmap.</summary>
public sealed record ProgressHeatCell(DateOnly Date, int Count, int Minutes);

// ---- Per-standard mastery gap map ----
public enum StandardMasteryStatus { NotStarted, Developing, Mastered }

/// <summary>A learning standard with the student's mastery against its KPM target band, plus a
/// remediation link (the next incomplete lesson mapped to that standard).</summary>
public sealed record StandardMasteryDto(
    Guid StandardId,
    string Code,
    string Strand,
    string Description,
    MasteryBand TargetBand,
    double Mastery,
    MasteryBand Band,
    int LessonsCompleted,
    int TotalLessons,
    StandardMasteryStatus Status,
    Guid? NextLessonId);

public sealed record SubjectStandardsMasteryDto(
    Guid SubjectId,
    string SubjectName,
    IReadOnlyList<StandardMasteryDto> Standards);

public sealed record BadgeDto(string Code, string Name, string? Icon, DateTimeOffset AwardedAt);

// ---- Diagnostic / placement test ----
public sealed record PlacementQuestionDto(string Key, string Prompt, QuestionType Type, IReadOnlyList<string> Options);

public sealed record PlacementTestDto(Guid SubjectId, string SubjectName, IReadOnlyList<PlacementQuestionDto> Questions);

public sealed record PlacementSubmitRequest(IReadOnlyDictionary<string, string> Answers);

public sealed record PlacementStandardScoreDto(string Code, string Strand, double Percent, MasteryBand Band);

public sealed record PlacementResultDto(
    Guid SubjectId,
    int Total,
    int Correct,
    double PercentScore,
    MasteryBand RecommendedBand,
    IReadOnlyList<PlacementStandardScoreDto> Standards);

// ---- Spaced-repetition review ----
public sealed record ReviewItemDto(
    Guid LessonId,
    string LessonTitle,
    string SubjectName,
    MasteryBand Band,
    int DaysSinceReview,
    int IntervalDays);

// ---- Mock exam ----
public sealed record ExamQuestionDto(string Key, string Prompt, QuestionType Type, IReadOnlyList<string> Options);

public sealed record ExamStartDto(
    Guid ExamId,
    Guid SubjectId,
    string SubjectName,
    int DurationSeconds,
    IReadOnlyList<ExamQuestionDto> Questions);

public sealed record ExamSubmitRequest(IReadOnlyDictionary<string, string> Answers, int ElapsedSeconds);

public sealed record ExamStandardScoreDto(string Code, string Strand, double Percent, MasteryBand Band);

public sealed record ExamResultDto(
    Guid ExamId,
    Guid SubjectId,
    string SubjectName,
    int QuestionCount,
    int CorrectCount,
    double PercentScore,
    MasteryBand Band,
    string Grade,
    int DurationSeconds,
    IReadOnlyList<ExamStandardScoreDto> Standards);

public sealed record ExamHistoryItemDto(
    Guid ExamId,
    Guid SubjectId,
    string SubjectName,
    double PercentScore,
    string Grade,
    MasteryBand Band,
    int DurationSeconds,
    DateTimeOffset TakenAt);

// ---- Adaptive recommendations ----
public enum RecommendationReason { Continue, Review, New }

/// <summary>A recommended next lesson for a student, with why it was chosen.</summary>
public sealed record LessonRecommendationDto(
    Guid LessonId,
    string LessonTitle,
    string SubjectName,
    string? StandardCode,
    RecommendationReason Reason);

// ---- Streak & daily goal ----
public sealed record StudentStreakDto(
    int CurrentStreak,
    int LongestStreak,
    bool ActiveToday,
    int TodayMinutes,
    int GoalMinutes,
    bool GoalMet,
    IReadOnlyList<DateOnly> ActiveDays);

// ---- Parent dashboard ----
public sealed record ParentDashboardDto(
    Guid HouseholdId,
    string HouseholdName,
    IReadOnlyList<ChildOverviewDto> Children,
    SubscriptionDto? Subscription,
    int OpenAiFlags);

public sealed record ChildOverviewDto(
    Guid StudentId,
    string DisplayName,
    string? Avatar,
    Level Level,
    double OverallMastery,
    MasteryBand OverallBand,
    int LessonsCompletedThisWeek,
    int MinutesThisWeek,
    IReadOnlyList<SubjectProgressDto> Subjects);

// ---- Study plans ----
public sealed record StudyPlanRequest(Guid StudentId, string Name, int TargetMinutesPerDay, IReadOnlyList<StudyPlanSlot> Schedule);
public sealed record StudyPlanSlot(string Day, Guid SubjectId, int Minutes);
public sealed record StudyPlanDto(Guid Id, Guid StudentId, string Name, int TargetMinutesPerDay, IReadOnlyList<StudyPlanSlot> Schedule, bool IsActive);
