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

/// <summary>Timed mock exams (UASA/PT3/SPM-style practice). A paper is assembled from a subject's
/// published lessons, snapshotted (with answers) on start so grading is stable, then graded on submit
/// into a stored <see cref="ExamAttempt"/> with a letter grade and per-standard analytics.</summary>
[ApiController]
[Authorize]
public sealed class ExamController : ControllerBase
{
    private const int MaxQuestionsPerLesson = 3;
    private const int MaxQuestions = 20;
    private const int SecondsPerQuestion = 90;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;
    private readonly ICurrentUser _current;
    private readonly IClock _clock;

    public ExamController(AppDbContext db, ICurrentUser current, IClock clock)
    {
        _db = db;
        _current = current;
        _clock = clock;
    }

    /// <summary>Assemble and start a timed paper. Returns client-safe questions (no answers) + duration.</summary>
    [HttpPost("/students/{id:guid}/subjects/{subjectId:guid}/exam/start")]
    public async Task<ActionResult<ExamStartDto>> Start(Guid id, Guid subjectId, CancellationToken ct)
    {
        await EnsureAccess(id, ct);
        var student = await _db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw ApiException.NotFound("Student");
        var subjectName = await _db.Subjects.Where(s => s.Id == subjectId).Select(s => s.Name).FirstOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("Subject");

        var stored = await AssembleAsync(student, subjectId, ct);
        if (stored.Count == 0)
        {
            throw ApiException.BadRequest("No exam questions are available for this subject yet.", "no_questions");
        }

        var exam = new ExamAttempt
        {
            StudentId = id,
            SubjectId = subjectId,
            SubjectName = subjectName,
            QuestionCount = stored.Count,
            DurationSeconds = stored.Count * SecondsPerQuestion,
            StartedAt = _clock.UtcNow,
            QuestionsJson = JsonSerializer.Serialize(stored, Json),
        };
        _db.ExamAttempts.Add(exam);
        await _db.SaveChangesAsync(ct);

        var clientQuestions = stored.Select(q => new ExamQuestionDto(q.Key, q.Prompt, q.Type, q.Options)).ToList();
        return Ok(new ExamStartDto(exam.Id, subjectId, subjectName, exam.DurationSeconds, clientQuestions));
    }

    /// <summary>Grade a started exam against its snapshot and store the result.</summary>
    [HttpPost("/students/{id:guid}/exam/{examId:guid}/submit")]
    public async Task<ActionResult<ExamResultDto>> Submit(Guid id, Guid examId, [FromBody] ExamSubmitRequest req, CancellationToken ct)
    {
        await EnsureAccess(id, ct);
        var exam = await _db.ExamAttempts.FirstOrDefaultAsync(e => e.Id == examId && e.StudentId == id, ct)
            ?? throw ApiException.NotFound("Exam");
        if (exam.SubmittedAt is not null)
        {
            throw ApiException.BadRequest("This exam has already been submitted.", "already_submitted");
        }

        var stored = JsonSerializer.Deserialize<List<StoredQuestion>>(exam.QuestionsJson, Json) ?? [];

        var correct = 0;
        var perStandard = new Dictionary<string, (string Strand, int Correct, int Total)>();
        foreach (var q in stored)
        {
            req.Answers.TryGetValue(q.Key, out var given);
            var isCorrect = string.Equals((given ?? "").Trim(), q.CorrectAnswer.Trim(), StringComparison.OrdinalIgnoreCase);
            if (isCorrect) correct++;

            if (q.StandardCode is { } code)
            {
                var agg = perStandard.GetValueOrDefault(code, (q.Strand ?? "", 0, 0));
                perStandard[code] = (agg.Strand, agg.Correct + (isCorrect ? 1 : 0), agg.Total + 1);
            }
        }

        var total = stored.Count;
        var percent = total == 0 ? 0 : Math.Round(correct * 100.0 / total, 1);
        var standards = perStandard
            .Select(kv => new ExamStandardScoreDto(
                kv.Key, kv.Value.Strand,
                kv.Value.Total == 0 ? 0 : Math.Round(kv.Value.Correct * 100.0 / kv.Value.Total, 1),
                MasteryMath.ToBand(kv.Value.Total == 0 ? 0 : kv.Value.Correct * 100.0 / kv.Value.Total)))
            .OrderBy(s => s.Code).ToList();

        exam.CorrectCount = correct;
        exam.PercentScore = percent;
        exam.Band = MasteryMath.ToBand(percent);
        exam.Grade = GradeFor(percent);
        exam.SubmittedAt = _clock.UtcNow;
        // Clamp the reported duration to the allotted time (don't trust the client blindly).
        exam.DurationSeconds = Math.Clamp(req.ElapsedSeconds, 0, exam.DurationSeconds);
        exam.StandardsJson = JsonSerializer.Serialize(standards, Json);
        await _db.SaveChangesAsync(ct);

        return Ok(new ExamResultDto(
            exam.Id, exam.SubjectId, exam.SubjectName, total, correct, percent,
            exam.Band, exam.Grade, exam.DurationSeconds, standards));
    }

    /// <summary>Past mock-exam results for a student (most recent first).</summary>
    [HttpGet("/students/{id:guid}/exams")]
    public async Task<ActionResult<IReadOnlyList<ExamHistoryItemDto>>> History(Guid id, CancellationToken ct)
    {
        await EnsureAccess(id, ct);
        var items = await _db.ExamAttempts.AsNoTracking()
            .Where(e => e.StudentId == id && e.SubmittedAt != null)
            .OrderByDescending(e => e.SubmittedAt)
            .Take(50)
            .Select(e => new ExamHistoryItemDto(
                e.Id, e.SubjectId, e.SubjectName, e.PercentScore, e.Grade, e.Band, e.DurationSeconds, e.SubmittedAt!.Value))
            .ToListAsync(ct);
        return Ok(items);
    }

    private async Task<List<StoredQuestion>> AssembleAsync(Student student, Guid subjectId, CancellationToken ct)
    {
        var lessons = await _db.Lessons.AsNoTracking()
            .Where(l => l.State == PublishState.Published
                        && l.SubjectVariant.SubjectId == subjectId
                        && l.SubjectVariant.SchoolType == student.SchoolType
                        && l.SubjectVariant.Language == student.PrimaryLanguage
                        && l.SubjectVariant.DlpMode == student.DlpMode)
            .OrderBy(l => l.SortOrder)
            .Select(l => new
            {
                l.Id,
                StandardCode = l.LearningStandard != null ? l.LearningStandard.Code : null,
                Strand = l.LearningStandard != null ? l.LearningStandard.Strand : null,
            })
            .ToListAsync(ct);

        var lessonIds = lessons.Select(l => l.Id).ToList();
        var activities = await _db.Activities.AsNoTracking()
            .Where(a => lessonIds.Contains(a.LessonId))
            .Select(a => new { a.Id, a.LessonId, a.QuestionsJson })
            .ToListAsync(ct);

        var result = new List<StoredQuestion>();
        foreach (var lesson in lessons)
        {
            var activity = activities.FirstOrDefault(a => a.LessonId == lesson.Id);
            if (activity is null) continue;

            var parsed = JsonSerializer.Deserialize<List<GradedQuestionJson>>(activity.QuestionsJson, Json) ?? [];
            foreach (var q in parsed.Take(MaxQuestionsPerLesson))
            {
                if (result.Count >= MaxQuestions) return result;
                var type = Enum.TryParse<QuestionType>(q.Type, out var t) ? t : QuestionType.ShortAnswer;
                result.Add(new StoredQuestion($"{activity.Id}|{q.Id}", lesson.StandardCode, lesson.Strand,
                    q.Prompt, type, q.Options ?? [], q.CorrectAnswer));
            }
        }
        return result;
    }

    private static string GradeFor(double pct) => pct switch
    {
        >= 90 => "A+",
        >= 80 => "A",
        >= 70 => "B+",
        >= 65 => "B",
        >= 60 => "C+",
        >= 50 => "C",
        >= 45 => "D",
        >= 40 => "E",
        _ => "G",
    };

    private async Task EnsureAccess(Guid studentId, CancellationToken ct)
    {
        if (_current.IsInRole(UserRole.Admin, UserRole.ContentAdmin, UserRole.SafetyReviewer)) return;
        if (_current.StudentId == studentId) return;
        var ok = await _db.StudentGuardians.AnyAsync(g => g.StudentId == studentId && g.GuardianUserId == _current.UserId, ct);
        if (!ok) throw ApiException.Forbidden("You don't have access to this student.");
    }

    private sealed record StoredQuestion(string Key, string? StandardCode, string? Strand, string Prompt,
        QuestionType Type, IReadOnlyList<string> Options, string CorrectAnswer);

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
