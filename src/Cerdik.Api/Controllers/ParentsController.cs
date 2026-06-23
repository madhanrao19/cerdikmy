using System.Text.Json;
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
[Authorize(Roles = "Parent,Admin")]
public sealed class ParentsController : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;
    private readonly ICurrentUser _current;

    public ParentsController(AppDbContext db, ICurrentUser current)
    {
        _db = db;
        _current = current;
    }

    [HttpGet("/parents/dashboard")]
    public async Task<ActionResult<ParentDashboardDto>> Dashboard(CancellationToken ct)
    {
        var userId = _current.UserId ?? throw ApiException.Unauthorized();
        var user = await _db.Users.Include(u => u.Household).FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw ApiException.Unauthorized();
        if (user.HouseholdId is not { } householdId)
        {
            throw ApiException.BadRequest("This account is not linked to a household.", "no_household");
        }

        var students = await _db.Students.AsNoTracking()
            .Where(s => s.HouseholdId == householdId && s.DeletedAt == null)
            .OrderBy(s => s.DisplayName)
            .ToListAsync(ct);

        var weekStart = DateTimeOffset.UtcNow.AddDays(-7);
        var children = new List<ChildOverviewDto>();
        foreach (var s in students)
        {
            var progress = await StudentsController.BuildProgress(_db, s.Id, s.DisplayName, ct);
            var lessonsThisWeek = await _db.ProgressRecords
                .CountAsync(p => p.StudentId == s.Id && p.Completed && p.CompletedAt >= weekStart, ct);
            var minutesThisWeek = await _db.Attempts
                .Where(a => a.StudentId == s.Id && a.SubmittedAt >= weekStart)
                .CountAsync(ct) * 5;

            children.Add(new ChildOverviewDto(
                s.Id, s.DisplayName, s.Avatar, s.Level,
                progress.OverallMastery, progress.OverallBand,
                lessonsThisWeek, minutesThisWeek, progress.Subjects));
        }

        var subscription = await _db.Subscriptions.Include(x => x.Invoices)
            .Where(x => x.HouseholdId == householdId)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var studentIds = students.Select(s => s.Id).ToList();
        var openFlags = await _db.ModerationEvents
            .CountAsync(m => m.InterventionRaised && m.ReviewedAt == null
                             && _db.TutorSessions.Any(t => t.Id == m.TutorSessionId && studentIds.Contains(t.StudentId)), ct);

        return Ok(new ParentDashboardDto(
            householdId, user.Household!.Name, children, subscription?.ToDto(), openFlags));
    }

    [HttpPost("/parents/study-plans")]
    public async Task<ActionResult<StudyPlanDto>> CreateStudyPlan([FromBody] StudyPlanRequest req, CancellationToken ct)
    {
        await EnsureGuardian(req.StudentId, ct);

        var plan = new StudyPlan
        {
            StudentId = req.StudentId,
            CreatedByUserId = _current.UserId!.Value,
            Name = string.IsNullOrWhiteSpace(req.Name) ? "Weekly Plan" : req.Name,
            TargetMinutesPerDay = Math.Clamp(req.TargetMinutesPerDay, 5, 240),
            ScheduleJson = JsonSerializer.Serialize(req.Schedule, Json),
            IsActive = true,
        };

        // Deactivate previous active plans for this student.
        var existing = await _db.StudyPlans.Where(p => p.StudentId == req.StudentId && p.IsActive).ToListAsync(ct);
        foreach (var p in existing) p.IsActive = false;

        _db.StudyPlans.Add(plan);
        await _db.SaveChangesAsync(ct);

        return Ok(new StudyPlanDto(plan.Id, plan.StudentId, plan.Name, plan.TargetMinutesPerDay, req.Schedule, plan.IsActive));
    }

    /// <summary>Lists a child's AI tutor conversations so a guardian can review them. Flagged
    /// sessions (NeedsReview / elevated risk) surface first.</summary>
    [HttpGet("/parents/students/{studentId:guid}/tutor-sessions")]
    public async Task<ActionResult<IReadOnlyList<TutorSessionSummaryDto>>> TutorSessions(Guid studentId, CancellationToken ct)
    {
        await EnsureGuardian(studentId, ct);

        var sessions = await _db.TutorSessions.AsNoTracking()
            .Where(s => s.StudentId == studentId)
            .Select(s => new TutorSessionSummaryDto(
                s.Id, s.StudentId, s.Title, s.Language, s.NeedsReview, s.HighestRisk,
                s.Messages.Count, s.CreatedAt,
                s.Messages.OrderByDescending(m => m.CreatedAt).Select(m => (DateTimeOffset?)m.CreatedAt).FirstOrDefault()))
            .ToListAsync(ct);

        // Flagged first, then most recent activity.
        return Ok(sessions
            .OrderByDescending(s => s.NeedsReview)
            .ThenByDescending(s => s.LastMessageAt ?? s.CreatedAt)
            .ToList());
    }

    /// <summary>Full transcript of one tutor session for guardian review.</summary>
    [HttpGet("/parents/tutor-sessions/{sessionId:guid}")]
    public async Task<ActionResult<TutorSessionDto>> TutorSessionTranscript(Guid sessionId, CancellationToken ct)
    {
        var session = await _db.TutorSessions.AsNoTracking()
            .Include(s => s.Messages).ThenInclude(m => m.Citations)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw ApiException.NotFound("Tutor session");

        await EnsureGuardian(session.StudentId, ct);

        return Ok(new TutorSessionDto(
            session.Id, session.StudentId, session.SubjectVariantId, session.Title,
            session.CurriculumVersionCode, session.SchoolType, session.Language, session.DlpMode,
            session.NeedsReview, session.HighestRisk,
            session.Messages.OrderBy(m => m.CreatedAt).Select(m => new TutorMessageDto(
                m.Id, m.Role, m.Content, m.MasterySignal, m.NeedsReview,
                m.Citations.OrderBy(c => c.Ordinal).Select(c => new CitationDto(
                    c.EmbeddingChunkId, c.LessonId, c.LessonTitle, c.Snippet, c.Score, c.Ordinal)).ToList(),
                m.CreatedAt)).ToList()));
    }

    private async Task EnsureGuardian(Guid studentId, CancellationToken ct)
    {
        if (_current.IsInRole(UserRole.Admin)) return;
        var ok = await _db.StudentGuardians.AnyAsync(g => g.StudentId == studentId && g.GuardianUserId == _current.UserId, ct);
        if (!ok) throw ApiException.Forbidden("You don't guardian this student.");
    }
}
