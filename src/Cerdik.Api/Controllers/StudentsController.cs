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
