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

public sealed record BadgeDto(string Code, string Name, string? Icon, DateTimeOffset AwardedAt);

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
