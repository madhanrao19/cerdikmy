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

/// <summary>Diagnostic placement: a short quiz drawn from a subject's existing lessons that estimates
/// where a student should start. Submitting it seeds an initial mastery baseline (without clobbering
/// any real progress) so recommendations and the standards map reflect the result immediately.</summary>
[ApiController]
[Authorize]
public sealed class PlacementController : ControllerBase
{
    private const int MaxQuestionsPerLesson = 2;
    private const int MaxQuestions = 12;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;
    private readonly ICurrentUser _current;
    private readonly IClock _clock;

    public PlacementController(AppDbContext db, ICurrentUser current, IClock clock)
    {
        _db = db;
        _current = current;
        _clock = clock;
    }

    /// <summary>Assemble the placement quiz for a subject (client-safe — no correct answers sent).</summary>
    [HttpGet("/students/{id:guid}/subjects/{subjectId:guid}/placement")]
    public async Task<ActionResult<PlacementTestDto>> Get(Guid id, Guid subjectId, CancellationToken ct)
    {
        await EnsureAccess(id, ct);
        var (subjectName, _, questions) = await BuildAsync(id, subjectId, ct);

        var dtos = questions.Select(q => new PlacementQuestionDto(q.Key, q.Prompt, q.Type, q.Options)).ToList();
        return Ok(new PlacementTestDto(subjectId, subjectName, dtos));
    }

    /// <summary>Grade the placement, seed an initial mastery baseline, and report per-standard results.</summary>
    [HttpPost("/students/{id:guid}/subjects/{subjectId:guid}/placement")]
    public async Task<ActionResult<PlacementResultDto>> Submit(Guid id, Guid subjectId, [FromBody] PlacementSubmitRequest req, CancellationToken ct)
    {
        await EnsureAccess(id, ct);
        var (_, lessons, questions) = await BuildAsync(id, subjectId, ct);

        // Grade EVERY assembled question — a skipped/blank answer counts as incorrect, so partial
        // submissions can't inflate the score and over-place the student.
        var total = questions.Count;
        var correct = 0;
        var perLesson = new Dictionary<Guid, (int Correct, int Total)>();
        foreach (var q in questions)
        {
            req.Answers.TryGetValue(q.Key, out var given);
            var isCorrect = string.Equals((given ?? "").Trim(), q.CorrectAnswer.Trim(), StringComparison.OrdinalIgnoreCase);
            if (isCorrect) correct++;
            var agg = perLesson.GetValueOrDefault(q.LessonId);
            perLesson[q.LessonId] = (agg.Correct + (isCorrect ? 1 : 0), agg.Total + 1);
        }

        // Seed an initial baseline for each covered lesson, but never overwrite existing progress.
        foreach (var (lessonId, agg) in perLesson)
        {
            if (agg.Total == 0) continue;
            var pct = Math.Round(agg.Correct * 100.0 / agg.Total, 1);
            var existing = await _db.ProgressRecords.FirstOrDefaultAsync(p => p.StudentId == id && p.LessonId == lessonId, ct);
            if (existing is not null) continue; // respect real attempt history

            var subjId = lessons.First(l => l.Id == lessonId).SubjectId;
            _db.ProgressRecords.Add(new ProgressRecord
            {
                StudentId = id,
                LessonId = lessonId,
                SubjectId = subjId,
                MasteryScore = pct,
                TahapPenguasaan = MasteryMath.ToBand(pct),
                LastActivityAt = _clock.UtcNow,
            });
        }
        await _db.SaveChangesAsync(ct);

        // Per-standard roll-up — only over lessons that actually contributed questions, so untested
        // standards (beyond the question cap) aren't reported as 0%/TP1.
        var askedLessonIds = perLesson.Keys.ToHashSet();
        var standards = lessons
            .Where(l => l.StandardCode is not null && askedLessonIds.Contains(l.Id))
            .GroupBy(l => (l.StandardCode!, l.Strand ?? ""))
            .Select(g =>
            {
                var c = g.Sum(l => perLesson.GetValueOrDefault(l.Id).Correct);
                var t = g.Sum(l => perLesson.GetValueOrDefault(l.Id).Total);
                var pct = t == 0 ? 0 : Math.Round(c * 100.0 / t, 1);
                return new PlacementStandardScoreDto(g.Key.Item1, g.Key.Item2, pct, MasteryMath.ToBand(pct));
            })
            .OrderBy(s => s.Code)
            .ToList();

        var overall = total == 0 ? 0 : Math.Round(correct * 100.0 / total, 1);
        return Ok(new PlacementResultDto(subjectId, total, correct, overall, MasteryMath.ToBand(overall), standards));
    }

    // ---- assembly shared by GET (questions) and POST (grading) ----
    private async Task<(string SubjectName, List<LessonRow> Lessons, List<PlacementQ> Questions)> BuildAsync(
        Guid studentId, Guid subjectId, CancellationToken ct)
    {
        var student = await _db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == studentId, ct)
            ?? throw ApiException.NotFound("Student");
        var subjectName = await _db.Subjects.Where(s => s.Id == subjectId).Select(s => s.Name).FirstOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("Subject");

        var lessons = await _db.Lessons.AsNoTracking()
            .Where(l => l.State == PublishState.Published
                        && l.SubjectVariant.SubjectId == subjectId
                        && l.SubjectVariant.SchoolType == student.SchoolType
                        && l.SubjectVariant.Language == student.PrimaryLanguage
                        && l.SubjectVariant.DlpMode == student.DlpMode)
            .OrderBy(l => l.SortOrder)
            .Select(l => new LessonRow(
                l.Id,
                l.SubjectVariant.SubjectId,
                l.LearningStandard != null ? l.LearningStandard.Code : null,
                l.LearningStandard != null ? l.LearningStandard.Strand : null))
            .ToListAsync(ct);

        var lessonIds = lessons.Select(l => l.Id).ToList();
        var activities = await _db.Activities.AsNoTracking()
            .Where(a => lessonIds.Contains(a.LessonId))
            .Select(a => new { a.Id, a.LessonId, a.QuestionsJson })
            .ToListAsync(ct);

        var questions = new List<PlacementQ>();
        foreach (var lesson in lessons)
        {
            var activity = activities.FirstOrDefault(a => a.LessonId == lesson.Id);
            if (activity is null) continue;

            var parsed = JsonSerializer.Deserialize<List<GradedQuestionJson>>(activity.QuestionsJson, Json) ?? [];
            foreach (var q in parsed.Take(MaxQuestionsPerLesson))
            {
                if (questions.Count >= MaxQuestions) break;
                var type = Enum.TryParse<QuestionType>(q.Type, out var t) ? t : QuestionType.ShortAnswer;
                questions.Add(new PlacementQ(
                    $"{activity.Id}|{q.Id}", lesson.Id, q.Prompt, type, q.Options ?? [], q.CorrectAnswer));
            }
            if (questions.Count >= MaxQuestions) break;
        }

        return (subjectName, lessons, questions);
    }

    private async Task EnsureAccess(Guid studentId, CancellationToken ct)
    {
        if (_current.IsInRole(UserRole.Admin, UserRole.ContentAdmin, UserRole.SafetyReviewer)) return;
        if (_current.StudentId == studentId) return;
        var ok = await _db.StudentGuardians.AnyAsync(g => g.StudentId == studentId && g.GuardianUserId == _current.UserId, ct);
        if (!ok) throw ApiException.Forbidden("You don't have access to this student.");
    }

    private sealed record LessonRow(Guid Id, Guid SubjectId, string? StandardCode, string? Strand);

    private sealed record PlacementQ(string Key, Guid LessonId, string Prompt, QuestionType Type,
        IReadOnlyList<string> Options, string CorrectAnswer);

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
