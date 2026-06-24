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
                var agg = perStandard.GetValueOrDefault(code, (Strand: q.Strand ?? "", Correct: 0, Total: 0));
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
        exam.Grade = Cerdik.Application.Grading.Grades.Letter(percent);
        exam.SubmittedAt = _clock.UtcNow;
        // Derive elapsed time from the server clock (StartedAt -> now), not the client-reported value,
        // so a suspended tab or a hand-crafted request can't make a timed exam effectively untimed.
        // Clamp to the allotted duration so a late/timed-out submission records at most the full time.
        var serverElapsed = (int)Math.Round((_clock.UtcNow - exam.StartedAt).TotalSeconds);
        exam.DurationSeconds = Math.Clamp(serverElapsed, 0, exam.DurationSeconds);
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

    private const double CertificatePassPercent = 50; // grade C or better

    /// <summary>Certificates earned by passing a mock exam (≥ 50%), most recent first.</summary>
    [HttpGet("/students/{id:guid}/certificates")]
    public async Task<ActionResult<IReadOnlyList<CertificateDto>>> Certificates(Guid id, CancellationToken ct)
    {
        await EnsureAccess(id, ct);
        var studentName = await _db.Students.Where(s => s.Id == id).Select(s => s.DisplayName).FirstOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("Student");

        var items = await _db.ExamAttempts.AsNoTracking()
            .Where(e => e.StudentId == id && e.SubmittedAt != null && e.PercentScore >= CertificatePassPercent)
            .OrderByDescending(e => e.SubmittedAt)
            .Take(50)
            .Select(e => new CertificateDto(
                e.Id, studentName, e.SubjectId, e.SubjectName, e.Grade, e.PercentScore, e.Band, e.SubmittedAt!.Value))
            .ToListAsync(ct);
        return Ok(items);
    }

    /// <summary>One certificate's detail for the printable page (404 unless the exam was passed).</summary>
    [HttpGet("/students/{id:guid}/certificates/{examId:guid}")]
    public async Task<ActionResult<CertificateDto>> Certificate(Guid id, Guid examId, CancellationToken ct)
    {
        await EnsureAccess(id, ct);
        var studentName = await _db.Students.Where(s => s.Id == id).Select(s => s.DisplayName).FirstOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("Student");

        var exam = await _db.ExamAttempts.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == examId && e.StudentId == id && e.SubmittedAt != null, ct)
            ?? throw ApiException.NotFound("Certificate");
        if (exam.PercentScore < CertificatePassPercent)
        {
            throw ApiException.NotFound("Certificate");
        }

        return Ok(new CertificateDto(
            exam.Id, studentName, exam.SubjectId, exam.SubjectName, exam.Grade, exam.PercentScore, exam.Band, exam.SubmittedAt!.Value));
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
