using System.Text.Json;
using System.Text.Json.Serialization;
using Cerdik.Application.Abstractions;
using Cerdik.Application.Dtos;
using Cerdik.Domain;
using Cerdik.Domain.Entities;
using Cerdik.Infrastructure.Jobs;
using Cerdik.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cerdik.Api.Controllers;

[ApiController]
[Authorize]
public sealed class AttemptsController : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;
    private readonly IClock _clock;

    public AttemptsController(AppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>Fetch an activity with client-safe questions (the correct answers are never sent to clients).</summary>
    [HttpGet("/activities/{id:guid}")]
    public async Task<ActionResult<ActivityDto>> GetActivity(Guid id, CancellationToken ct)
    {
        var activity = await _db.Activities.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw ApiException.NotFound("Activity");

        var graded = JsonSerializer.Deserialize<List<GradedQuestionJson>>(activity.QuestionsJson, Json) ?? [];
        var questions = graded.Select(q => new QuestionDto(
            q.Id, q.Prompt, Enum.TryParse<QuestionType>(q.Type, out var t) ? t : QuestionType.ShortAnswer,
            q.Options, Math.Max(1, q.Points), q.Hint)).ToList();

        return Ok(new ActivityDto(activity.Id, activity.LessonId, activity.Title, activity.Type,
            activity.MaxScore, activity.PassThresholdPercent, questions));
    }

    [HttpPost("/activities/{id:guid}/start")]
    public async Task<ActionResult<AttemptDto>> Start(Guid id, [FromBody] StartActivityRequest req, CancellationToken ct)
    {
        var activity = await _db.Activities.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw ApiException.NotFound("Activity");
        await EnsureStudentAccess(req.StudentId, ct);

        var attempt = new Attempt
        {
            ActivityId = activity.Id,
            StudentId = req.StudentId,
            Status = AttemptStatus.InProgress,
            MaxScore = activity.MaxScore,
            StartedAt = _clock.UtcNow,
        };
        _db.Attempts.Add(attempt);
        await _db.SaveChangesAsync(ct);

        return Ok(ToDto(attempt));
    }

    [HttpPost("/attempts/{id:guid}/submit")]
    public async Task<ActionResult<AttemptResultDto>> Submit(Guid id, [FromBody] SubmitAttemptRequest req, CancellationToken ct)
    {
        var attempt = await _db.Attempts.Include(a => a.Activity).ThenInclude(a => a.Lesson)
            .FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw ApiException.NotFound("Attempt");
        if (attempt.Status == AttemptStatus.Submitted || attempt.Status == AttemptStatus.Graded)
        {
            throw ApiException.BadRequest("Attempt already submitted.", "already_submitted");
        }
        await EnsureStudentAccess(attempt.StudentId, ct);

        var questions = JsonSerializer.Deserialize<List<GradedQuestionJson>>(attempt.Activity.QuestionsJson, Json) ?? [];
        var results = new List<QuestionResultDto>();
        var score = 0;
        foreach (var q in questions)
        {
            req.Answers.TryGetValue(q.Id, out var given);
            given ??= string.Empty;
            var correct = string.Equals(given.Trim(), q.CorrectAnswer.Trim(), StringComparison.OrdinalIgnoreCase);
            if (correct) score += Math.Max(1, q.Points);
            results.Add(new QuestionResultDto(q.Id, correct, given, q.CorrectAnswer, q.Explanation));
        }

        var maxScore = questions.Sum(q => Math.Max(1, q.Points));
        var percent = maxScore == 0 ? 0 : Math.Round(score * 100.0 / maxScore, 1);
        var passed = percent >= attempt.Activity.PassThresholdPercent;

        attempt.Status = AttemptStatus.Graded;
        attempt.SubmittedAt = _clock.UtcNow;
        attempt.Score = score;
        attempt.MaxScore = maxScore;
        attempt.PercentScore = percent;
        attempt.Passed = passed;
        attempt.AnswersJson = JsonSerializer.Serialize(req.Answers, Json);
        attempt.ResultJson = JsonSerializer.Serialize(results, Json);

        var band = await UpdateProgress(attempt, percent, ct);
        await _db.SaveChangesAsync(ct);

        return Ok(new AttemptResultDto(attempt.Id, score, maxScore, percent, passed, band, results));
    }

    /// <summary>Update (or create) the per-lesson progress roll-up using an EWMA mastery score.</summary>
    private async Task<MasteryBand> UpdateProgress(Attempt attempt, double percent, CancellationToken ct)
    {
        var lessonId = attempt.Activity.LessonId;
        var record = await _db.ProgressRecords.FirstOrDefaultAsync(p => p.StudentId == attempt.StudentId && p.LessonId == lessonId, ct);
        if (record is null)
        {
            record = new ProgressRecord
            {
                StudentId = attempt.StudentId,
                LessonId = lessonId,
                SubjectId = attempt.Activity.Lesson.SubjectVariantId, // resolved to subject below
            };
            // Resolve the subject id from the variant.
            record.SubjectId = await _db.SubjectVariants.Where(v => v.Id == attempt.Activity.Lesson.SubjectVariantId)
                .Select(v => v.SubjectId).FirstOrDefaultAsync(ct);
            _db.ProgressRecords.Add(record);
        }

        record.MasteryScore = MasteryMath.UpdateScore(record.MasteryScore, percent);
        record.TahapPenguasaan = MasteryMath.ToBand(record.MasteryScore);
        record.AttemptCount += 1;
        record.LastActivityAt = _clock.UtcNow;
        if (attempt.Passed && !record.Completed)
        {
            record.Completed = true;
            record.CompletedAt = _clock.UtcNow;

            // Award points + a first-lesson badge.
            var student = await _db.Students.FirstOrDefaultAsync(s => s.Id == attempt.StudentId, ct);
            if (student is not null)
            {
                student.Points += 10;
                if (!await _db.Badges.AnyAsync(b => b.StudentId == student.Id && b.Code == "first-lesson", ct))
                {
                    _db.Badges.Add(new Badge { StudentId = student.Id, Code = "first-lesson", Name = "First Lesson Complete", Icon = "🎯" });
                }
            }
        }
        return record.TahapPenguasaan;
    }

    private async Task EnsureStudentAccess(Guid studentId, CancellationToken ct)
    {
        var current = HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
        if (current.Role == UserRole.Admin || current.Role == UserRole.ContentAdmin) return;
        if (current.StudentId == studentId) return;

        // Parent: must guardian the student.
        var ok = await _db.StudentGuardians.AnyAsync(g => g.StudentId == studentId && g.GuardianUserId == current.UserId, ct);
        if (!ok) throw ApiException.Forbidden("You don't have access to this student.");
    }

    private static AttemptDto ToDto(Attempt a) =>
        new(a.Id, a.ActivityId, a.StudentId, a.Status, a.Score, a.MaxScore, a.PercentScore, a.Passed, a.StartedAt, a.SubmittedAt);

    private sealed record GradedQuestionJson(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("options")] string[] Options,
        [property: JsonPropertyName("points")] int Points,
        [property: JsonPropertyName("hint")] string? Hint,
        [property: JsonPropertyName("correctAnswer")] string CorrectAnswer,
        [property: JsonPropertyName("explanation")] string? Explanation);
}
