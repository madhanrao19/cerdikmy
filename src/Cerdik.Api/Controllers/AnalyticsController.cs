using Cerdik.Application.Dtos;
using Cerdik.Domain;
using Cerdik.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cerdik.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin,ContentAdmin,SafetyReviewer")]
public sealed class AnalyticsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AnalyticsController(AppDbContext db) => _db = db;

    [HttpGet("/analytics/cohorts")]
    public async Task<ActionResult<CohortAnalyticsDto>> Cohorts(CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-7);

        var totalStudents = await _db.Students.CountAsync(s => s.DeletedAt == null, ct);
        var active = await _db.Attempts.Where(a => a.SubmittedAt >= since).Select(a => a.StudentId).Distinct().CountAsync(ct);
        var lessons7d = await _db.ProgressRecords.CountAsync(p => p.Completed && p.CompletedAt >= since, ct);
        var sessions7d = await _db.TutorSessions.CountAsync(t => t.CreatedAt >= since, ct);
        var openFlags = await _db.ModerationEvents.CountAsync(m => m.InterventionRaised && m.ReviewedAt == null, ct);

        // By level.
        var students = await _db.Students.AsNoTracking().Where(s => s.DeletedAt == null)
            .Select(s => new { s.Id, s.Level, s.SchoolType }).ToListAsync(ct);
        var progressByStudent = await _db.ProgressRecords.AsNoTracking()
            .GroupBy(p => p.StudentId)
            .Select(g => new { StudentId = g.Key, Avg = g.Average(x => x.MasteryScore), Completed = g.Count(x => x.Completed) })
            .ToListAsync(ct);
        var pmap = progressByStudent.ToDictionary(x => x.StudentId);

        CohortRow Row(string key, IEnumerable<Guid> ids)
        {
            var list = ids.ToList();
            var withProgress = list.Where(id => pmap.ContainsKey(id)).Select(id => pmap[id]).ToList();
            var avg = withProgress.Count > 0 ? Math.Round(withProgress.Average(x => x.Avg), 1) : 0;
            var completed = withProgress.Sum(x => x.Completed);
            return new CohortRow(key, list.Count, avg, completed);
        }

        var byLevel = students.GroupBy(s => s.Level)
            .Select(g => Row(g.Key.ToString(), g.Select(x => x.Id))).ToList();
        var bySchool = students.GroupBy(s => s.SchoolType)
            .Select(g => Row(g.Key.ToString(), g.Select(x => x.Id))).ToList();

        return Ok(new CohortAnalyticsDto(totalStudents, active, lessons7d, sessions7d, openFlags, byLevel, bySchool));
    }
}
