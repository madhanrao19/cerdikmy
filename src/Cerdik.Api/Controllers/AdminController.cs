using Cerdik.Application.Abstractions;
using Cerdik.Application.Dtos;
using Cerdik.Domain;
using Cerdik.Domain.Entities;
using Cerdik.Infrastructure.Jobs;
using Cerdik.Infrastructure.Persistence;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cerdik.Api.Controllers;

[ApiController]
[Route("admin")]
[Authorize(Roles = "Admin,ContentAdmin,SafetyReviewer")]
public sealed class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ICurrentUser _current;

    public AdminController(AppDbContext db, IPasswordHasher hasher, ICurrentUser current)
    {
        _db = db;
        _hasher = hasher;
        _current = current;
    }

    // ---- Users (platform admins only; ContentAdmin/SafetyReviewer must not read account PII) ----
    [HttpGet("users")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IReadOnlyList<AdminUserDto>>> Users([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var query = _db.Users.AsNoTracking().Where(u => u.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(u => u.Email.Contains(search) || (u.FullName != null && u.FullName.Contains(search)));

        var users = await query.OrderByDescending(u => u.CreatedAt)
            .Skip((Math.Max(1, page) - 1) * pageSize).Take(Math.Clamp(pageSize, 1, 100))
            .ToListAsync(ct);

        return Ok(users.Select(u => new AdminUserDto(u.Id, u.Email, u.FullName, u.Role, u.IsActive, u.HouseholdId, u.CreatedAt, u.LastLoginAt)).ToList());
    }

    [HttpPost("users")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<AdminUserDto>> CreateUser([FromBody] CreateAdminUserRequest req, CancellationToken ct)
    {
        Validate.Email(req.Email);
        Validate.Password(req.Password);
        if (await _db.Users.AnyAsync(u => u.Email == req.Email, ct)) throw ApiException.Conflict("Email already in use.");

        var orgId = _current.OrganizationId ?? (await _db.Organizations.Select(o => o.Id).FirstAsync(ct));
        var user = new User
        {
            OrganizationId = orgId,
            Email = req.Email,
            FullName = req.FullName,
            Role = req.Role,
            PasswordHash = _hasher.Hash(req.Password),
            EmailConfirmed = true,
        };
        _db.Users.Add(user);
        await Audit("user.create", "User", user.Id.ToString(), ct);
        await _db.SaveChangesAsync(ct);

        return Ok(new AdminUserDto(user.Id, user.Email, user.FullName, user.Role, user.IsActive, user.HouseholdId, user.CreatedAt, user.LastLoginAt));
    }

    // ---- Content ----
    [HttpGet("content")]
    public async Task<ActionResult<IReadOnlyList<AdminContentItemDto>>> Content([FromQuery] PublishState? state, CancellationToken ct)
    {
        var query = _db.Lessons.AsNoTracking()
            .Include(l => l.SubjectVariant).ThenInclude(v => v.Subject)
            .Include(l => l.Blocks).Include(l => l.Activities)
            .AsQueryable();
        if (state is { } s) query = query.Where(l => l.State == s);

        var lessons = await query.OrderByDescending(l => l.UpdatedAt).Take(200).ToListAsync(ct);
        return Ok(lessons.Select(l => new AdminContentItemDto(
            l.Id, l.Title, l.SubjectVariant.Subject.Name, l.SubjectVariant.SchoolType, l.SubjectVariant.Language,
            l.SubjectVariant.DlpMode, l.State, l.Blocks.Count, l.Activities.Count, l.UpdatedAt)).ToList());
    }

    [HttpPost("content/import")]
    [Authorize(Roles = "Admin,ContentAdmin")]
    public async Task<ActionResult<AdminContentItemDto>> Import([FromBody] ImportContentRequest req, CancellationToken ct)
    {
        var variant = await _db.SubjectVariants.Include(v => v.Subject)
            .FirstOrDefaultAsync(v => v.Id == req.SubjectVariantId, ct) ?? throw ApiException.NotFound("Subject variant");

        Guid? standardId = null;
        if (!string.IsNullOrWhiteSpace(req.LearningStandardCode))
        {
            standardId = await _db.LearningStandards
                .Where(s => s.SubjectId == variant.SubjectId && s.Code == req.LearningStandardCode)
                .Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        }

        var lesson = new Lesson
        {
            SubjectVariantId = variant.Id,
            LearningStandardId = standardId,
            Slug = Slugify(req.Title),
            Title = req.Title,
            Summary = req.Summary,
            State = PublishState.Draft,
        };
        var order = 0;
        foreach (var b in req.Blocks)
        {
            lesson.Blocks.Add(new LessonBlock { Type = b.Type, SortOrder = order++, Markdown = b.Markdown, ConfigJson = b.ConfigJson });
        }
        _db.Lessons.Add(lesson);
        await Audit("content.import", "Lesson", lesson.Id.ToString(), ct);
        await _db.SaveChangesAsync(ct);

        return Ok(new AdminContentItemDto(lesson.Id, lesson.Title, variant.Subject.Name, variant.SchoolType, variant.Language, variant.DlpMode, lesson.State, lesson.Blocks.Count, 0, lesson.UpdatedAt));
    }

    [HttpPost("content/publish")]
    [Authorize(Roles = "Admin,ContentAdmin")]
    public async Task<IActionResult> Publish([FromBody] PublishContentRequest req, CancellationToken ct)
    {
        var lesson = await _db.Lessons.FirstOrDefaultAsync(l => l.Id == req.LessonId, ct) ?? throw ApiException.NotFound("Lesson");
        lesson.State = req.Publish ? PublishState.Published : PublishState.Unpublished;
        lesson.PublishedAt = req.Publish ? DateTimeOffset.UtcNow : null;
        lesson.PublishedByUserId = req.Publish ? _current.UserId : null;
        await Audit(req.Publish ? "content.publish" : "content.unpublish", "Lesson", lesson.Id.ToString(), ct);
        await _db.SaveChangesAsync(ct);

        // Re-index the RAG corpus in the background (approves/removes chunks for retrieval).
        BackgroundJob.Enqueue<BackgroundJobs>(j => j.IndexLessonAsync(lesson.Id));
        return NoContent();
    }

    // ---- Moderation queue ----
    [HttpGet("moderation")]
    [Authorize(Roles = "Admin,SafetyReviewer")]
    public async Task<ActionResult<IReadOnlyList<ModerationQueueItemDto>>> Moderation([FromQuery] bool onlyOpen = true, CancellationToken ct = default)
    {
        var query = _db.ModerationEvents.AsNoTracking().Where(m => m.InterventionRaised);
        if (onlyOpen) query = query.Where(m => m.ReviewedAt == null);

        var events = await query.OrderByDescending(m => m.CreatedAt).Take(200).ToListAsync(ct);
        var sessionIds = events.Select(e => e.TutorSessionId).Distinct().ToList();
        var sessions = await _db.TutorSessions.AsNoTracking().Where(s => sessionIds.Contains(s.Id))
            .Select(s => new { s.Id, s.StudentId, s.Student.DisplayName }).ToListAsync(ct);
        var map = sessions.ToDictionary(s => s.Id);

        return Ok(events.Select(m =>
        {
            map.TryGetValue(m.TutorSessionId, out var sess);
            return new ModerationQueueItemDto(
                m.Id, m.TutorSessionId, sess?.StudentId ?? Guid.Empty, sess?.DisplayName ?? "—",
                m.Stage, m.Decision, m.Risk, m.Categories, m.Reason, m.InterventionRaised,
                m.ReviewedAt != null, m.CreatedAt);
        }).ToList());
    }

    [HttpPost("moderation/review")]
    [Authorize(Roles = "Admin,SafetyReviewer")]
    public async Task<IActionResult> ReviewModeration([FromBody] ReviewModerationRequest req, CancellationToken ct)
    {
        var ev = await _db.ModerationEvents.FirstOrDefaultAsync(m => m.Id == req.EventId, ct) ?? throw ApiException.NotFound("Moderation event");
        ev.Decision = req.Decision;
        ev.ReviewedByUserId = _current.UserId;
        ev.ReviewedAt = DateTimeOffset.UtcNow;
        ev.ReviewNotes = req.Notes;

        // If no other open interventions remain on the session, clear its review flag.
        var openRemaining = await _db.ModerationEvents
            .CountAsync(m => m.TutorSessionId == ev.TutorSessionId && m.InterventionRaised && m.ReviewedAt == null && m.Id != ev.Id, ct);
        if (openRemaining == 0)
        {
            var session = await _db.TutorSessions.FirstOrDefaultAsync(s => s.Id == ev.TutorSessionId, ct);
            if (session is not null) session.NeedsReview = false;
        }
        await Audit("moderation.review", "ModerationEvent", ev.Id.ToString(), ct);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- Payment / webhook logs ----
    [HttpGet("payments")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IReadOnlyList<WebhookLogDto>>> Payments(CancellationToken ct)
    {
        var payments = await _db.Payments.AsNoTracking().OrderByDescending(p => p.CreatedAt).Take(200).ToListAsync(ct);
        return Ok(payments.Select(p => new WebhookLogDto(p.Id, p.Provider, p.ProviderPaymentId, p.Status, p.AmountCents, p.Currency, p.ProcessedAt, p.CreatedAt)).ToList());
    }

    private async Task Audit(string action, string entityType, string entityId, CancellationToken ct)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            OrganizationId = _current.OrganizationId,
            ActorUserId = _current.UserId,
            ActorEmail = _current.Email,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Ip = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers.UserAgent.ToString(),
        });
        await Task.CompletedTask;
    }

    private static string Slugify(string title)
    {
        var slug = new string(title.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-') + "-" + Guid.NewGuid().ToString("N")[..6];
    }
}
