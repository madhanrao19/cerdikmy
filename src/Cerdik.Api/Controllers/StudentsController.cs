using Cerdik.Application.Abstractions;
using Cerdik.Application.Dtos;
using Cerdik.Domain;
using Cerdik.Infrastructure.Jobs;
using Cerdik.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cerdik.Api.Controllers;

[ApiController]
[Authorize]
public sealed class StudentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _current;

    public StudentsController(AppDbContext db, ICurrentUser current)
    {
        _db = db;
        _current = current;
    }

    [HttpGet("/students/{id:guid}/progress")]
    public async Task<ActionResult<ProgressDto>> Progress(Guid id, CancellationToken ct)
    {
        await EnsureAccess(id, ct);
        var student = await _db.Students.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw ApiException.NotFound("Student");

        return Ok(await BuildProgress(_db, id, student.DisplayName, ct));
    }

    /// <summary>Per-learning-standard mastery for a subject: which KPM standards the student has
    /// mastered, which are still developing, and which are untouched — with a remediation link.</summary>
    [HttpGet("/students/{id:guid}/subjects/{subjectId:guid}/standards-mastery")]
    public async Task<ActionResult<SubjectStandardsMasteryDto>> StandardsMastery(Guid id, Guid subjectId, CancellationToken ct)
    {
        await EnsureAccess(id, ct);
        var subject = await _db.Subjects.AsNoTracking().FirstOrDefaultAsync(s => s.Id == subjectId, ct)
            ?? throw ApiException.NotFound("Subject");

        var standards = await _db.LearningStandards.AsNoTracking()
            .Where(s => s.SubjectId == subjectId)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Code)
            .ToListAsync(ct);

        // Published lessons for this subject that are mapped to a standard.
        var lessons = await _db.Lessons.AsNoTracking()
            .Where(l => l.SubjectVariant.SubjectId == subjectId
                        && l.LearningStandardId != null
                        && l.State == PublishState.Published)
            .Select(l => new { l.Id, l.LearningStandardId, l.SortOrder })
            .ToListAsync(ct);

        var records = await _db.ProgressRecords.AsNoTracking()
            .Where(p => p.StudentId == id)
            .Select(p => new { p.LessonId, p.MasteryScore, p.Completed })
            .ToListAsync(ct);
        var recordByLesson = records
            .GroupBy(r => r.LessonId)
            .ToDictionary(g => g.Key, g => g.First());

        var result = new List<StandardMasteryDto>(standards.Count);
        foreach (var std in standards)
        {
            var stdLessons = lessons.Where(l => l.LearningStandardId == std.Id)
                .OrderBy(l => l.SortOrder).ToList();
            var touched = stdLessons
                .Where(l => recordByLesson.ContainsKey(l.Id))
                .Select(l => recordByLesson[l.Id])
                .ToList();

            var mastery = touched.Count > 0 ? Math.Round(touched.Average(r => r.MasteryScore), 1) : 0;
            var band = MasteryMath.ToBand(mastery);
            var completed = touched.Count(r => r.Completed);

            var status = touched.Count == 0
                ? StandardMasteryStatus.NotStarted
                : (int)band >= (int)std.TargetBand ? StandardMasteryStatus.Mastered
                : StandardMasteryStatus.Developing;

            // Remediation link: first lesson not yet completed (else the first lesson).
            var nextLesson = stdLessons.FirstOrDefault(l =>
                !recordByLesson.TryGetValue(l.Id, out var r) || !r.Completed) ?? stdLessons.FirstOrDefault();

            result.Add(new StandardMasteryDto(
                std.Id, std.Code, std.Strand, std.Description, std.TargetBand,
                mastery, band, completed, stdLessons.Count, status, nextLesson?.Id));
        }

        return Ok(new SubjectStandardsMasteryDto(subjectId, subject.Name, result));
    }

    /// <summary>Daily learning streak and today's goal progress, computed from attempt/lesson activity
    /// (UTC day boundaries) and the student's active study-plan target.</summary>
    [HttpGet("/students/{id:guid}/streak")]
    public async Task<ActionResult<StudentStreakDto>> Streak(Guid id, CancellationToken ct)
    {
        await EnsureAccess(id, ct);

        var since = DateTimeOffset.UtcNow.AddDays(-400);
        var attemptTimes = await _db.Attempts.AsNoTracking()
            .Where(a => a.StudentId == id && a.SubmittedAt >= since)
            .Select(a => a.SubmittedAt!.Value).ToListAsync(ct);
        var lessonTimes = await _db.ProgressRecords.AsNoTracking()
            .Where(p => p.StudentId == id && p.CompletedAt >= since)
            .Select(p => p.CompletedAt!.Value).ToListAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var days = attemptTimes.Concat(lessonTimes)
            .Select(t => DateOnly.FromDateTime(t.UtcDateTime))
            .Distinct().OrderBy(d => d).ToList();
        var set = days.ToHashSet();

        // Current streak: walk back from today (or yesterday, so a streak stays "alive" until midnight).
        var current = 0;
        DateOnly? cursor = set.Contains(today) ? today
            : set.Contains(today.AddDays(-1)) ? today.AddDays(-1) : null;
        while (cursor is { } c && set.Contains(c)) { current++; cursor = c.AddDays(-1); }

        // Longest streak across the window.
        var longest = 0;
        var run = 0;
        DateOnly? prev = null;
        foreach (var d in days)
        {
            run = prev is { } p && d == p.AddDays(1) ? run + 1 : 1;
            longest = Math.Max(longest, run);
            prev = d;
        }

        var todayMinutes = attemptTimes.Count(t => DateOnly.FromDateTime(t.UtcDateTime) == today) * 5;
        var goal = await _db.StudyPlans.AsNoTracking()
            .Where(p => p.StudentId == id && p.IsActive)
            .Select(p => (int?)p.TargetMinutesPerDay).FirstOrDefaultAsync(ct) ?? 20;

        var recent = days.Where(d => d >= today.AddDays(-13)).ToList();

        return Ok(new StudentStreakDto(
            current, longest, set.Contains(today), todayMinutes, goal, todayMinutes >= goal, recent));
    }

    /// <summary>Shared progress builder reused by the parent dashboard.</summary>
    internal static async Task<ProgressDto> BuildProgress(AppDbContext db, Guid studentId, string studentName, CancellationToken ct)
    {
        var records = await db.ProgressRecords.AsNoTracking().Where(p => p.StudentId == studentId).ToListAsync(ct);

        var subjectIds = records.Where(r => r.SubjectId != null).Select(r => r.SubjectId!.Value).Distinct().ToList();
        var subjects = await db.Subjects.AsNoTracking().Where(s => subjectIds.Contains(s.Id)).ToListAsync(ct);

        var subjectProgress = new List<SubjectProgressDto>();
        foreach (var subj in subjects)
        {
            var subjRecords = records.Where(r => r.SubjectId == subj.Id).ToList();
            var totalLessons = await db.Lessons.CountAsync(l => l.SubjectVariant.SubjectId == subj.Id && l.State == PublishState.Published, ct);
            var avgMastery = subjRecords.Count > 0 ? Math.Round(subjRecords.Average(r => r.MasteryScore), 1) : 0;
            subjectProgress.Add(new SubjectProgressDto(
                subj.Id, subj.Name, avgMastery, MasteryMath.ToBand(avgMastery),
                subjRecords.Count(r => r.Completed), totalLessons,
                subjRecords.Max(r => (DateTimeOffset?)r.LastActivityAt)));
        }

        var overall = records.Count > 0 ? Math.Round(records.Average(r => r.MasteryScore), 1) : 0;
        var totalAll = await db.Lessons.CountAsync(l => l.State == PublishState.Published, ct);

        // Heatmap: last 84 days of attempt activity.
        var since = DateTimeOffset.UtcNow.AddDays(-84);
        var attempts = await db.Attempts.AsNoTracking()
            .Where(a => a.StudentId == studentId && a.SubmittedAt >= since)
            .Select(a => a.SubmittedAt!.Value)
            .ToListAsync(ct);
        var heatmap = attempts
            .GroupBy(d => DateOnly.FromDateTime(d.UtcDateTime))
            .Select(g => new ProgressHeatCell(g.Key, g.Count(), g.Count() * 5))
            .OrderBy(c => c.Date)
            .ToList();

        var badges = await db.Badges.AsNoTracking().Where(b => b.StudentId == studentId)
            .OrderByDescending(b => b.AwardedAt)
            .Select(b => new BadgeDto(b.Code, b.Name, b.Icon, b.AwardedAt))
            .ToListAsync(ct);

        return new ProgressDto(
            studentId, studentName, overall, MasteryMath.ToBand(overall),
            records.Count(r => r.Completed), totalAll, subjectProgress, heatmap, badges);
    }

    private async Task EnsureAccess(Guid studentId, CancellationToken ct)
    {
        if (_current.IsInRole(UserRole.Admin, UserRole.ContentAdmin, UserRole.SafetyReviewer)) return;
        if (_current.StudentId == studentId) return;
        var ok = await _db.StudentGuardians.AnyAsync(g => g.StudentId == studentId && g.GuardianUserId == _current.UserId, ct);
        if (!ok) throw ApiException.Forbidden("You don't have access to this student.");
    }
}
