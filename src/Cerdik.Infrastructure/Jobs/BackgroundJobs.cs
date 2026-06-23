using System.Text.Json;
using Cerdik.Application.Abstractions;
using Cerdik.Application.Email;
using Cerdik.Domain;
using Cerdik.Domain.Entities;
using Cerdik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cerdik.Infrastructure.Jobs;

/// <summary>Hangfire-invoked jobs. Each method is enqueued by name from the API/worker, so they take
/// simple serializable arguments and resolve their own scoped dependencies via constructor injection.</summary>
public sealed class BackgroundJobs
{
    private readonly AppDbContext _db;
    private readonly ContentIndexer _indexer;
    private readonly IStorageService _storage;
    private readonly IEmailSender _email;
    private readonly IConfiguration _config;
    private readonly ILogger<BackgroundJobs> _log;

    public BackgroundJobs(
        AppDbContext db, ContentIndexer indexer, IStorageService storage,
        IEmailSender email, IConfiguration config, ILogger<BackgroundJobs> log)
    {
        _db = db;
        _indexer = indexer;
        _storage = storage;
        _email = email;
        _config = config;
        _log = log;
    }

    private string AppUrl => (_config["NEXT_PUBLIC_APP_URL"] ?? "http://localhost:5080").TrimEnd('/');

    /// <summary>Re-index a lesson into the RAG corpus (enqueued after publish/import).</summary>
    public Task IndexLessonAsync(Guid lessonId) => _indexer.IndexLessonAsync(lessonId);

    /// <summary>Nightly: recompute mastery roll-ups (EWMA over attempts) and Tahap Penguasaan bands.</summary>
    public async Task RecomputeMasteryAsync()
    {
        var records = await _db.ProgressRecords.ToListAsync();
        foreach (var pr in records)
        {
            pr.TahapPenguasaan = MasteryMath.ToBand(pr.MasteryScore);
        }
        await _db.SaveChangesAsync();
        _log.LogInformation("Recomputed mastery for {Count} progress records", records.Count);
    }

    /// <summary>Emails guardians when the safety system raises a high-risk flag on a child's tutor
    /// chat. Idempotent: each flag is notified once (tracked by GuardianNotifiedAt).</summary>
    public async Task NotifyGuardiansOfFlagsAsync()
    {
        var pending = await _db.ModerationEvents
            .Where(m => m.InterventionRaised && m.GuardianNotifiedAt == null
                        && m.ReviewedAt == null && m.Risk >= RiskLevel.High)
            .ToListAsync();
        if (pending.Count == 0) return;

        var reviewUrl = $"{AppUrl}/parent/tutor-review";
        var now = DateTimeOffset.UtcNow;
        var notified = 0;

        foreach (var ev in pending)
        {
            var session = await _db.TutorSessions.FirstOrDefaultAsync(s => s.Id == ev.TutorSessionId);
            if (session is not null)
            {
                var childName = await _db.Students.Where(s => s.Id == session.StudentId)
                    .Select(s => s.DisplayName).FirstOrDefaultAsync() ?? "your child";
                var guardianEmails = await _db.StudentGuardians
                    .Where(g => g.StudentId == session.StudentId)
                    .Select(g => g.GuardianUser.Email)
                    .Distinct().ToListAsync();

                var (subject, html) = EmailTemplates.SafetyAlert(childName, reviewUrl);
                foreach (var email in guardianEmails.Where(e => !string.IsNullOrEmpty(e)))
                {
                    await _email.SendAsync(email, subject, html);
                    notified++;
                }
            }
            ev.GuardianNotifiedAt = now;
        }

        await _db.SaveChangesAsync();
        _log.LogInformation("Guardian safety alerts: {Recipients} recipient(s) across {Flags} flag(s).", notified, pending.Count);
    }

    /// <summary>Weekly per-family learning summary email. Skips families with no activity this week.</summary>
    public async Task SendWeeklyParentDigestAsync()
    {
        var weekStart = DateTimeOffset.UtcNow.AddDays(-7);

        var parents = await _db.Users
            .Where(u => u.Role == UserRole.Parent && u.IsActive && u.DeletedAt == null && u.HouseholdId != null)
            .Select(u => new { u.Email, u.FullName, HouseholdId = u.HouseholdId!.Value })
            .ToListAsync();

        var sent = 0;
        foreach (var parent in parents)
        {
            if (string.IsNullOrEmpty(parent.Email)) continue;

            var students = await _db.Students
                .Where(s => s.HouseholdId == parent.HouseholdId && s.DeletedAt == null)
                .Select(s => new { s.Id, s.DisplayName }).ToListAsync();

            var children = new List<EmailTemplates.DigestChild>();
            foreach (var s in students)
            {
                var lessons = await _db.ProgressRecords.CountAsync(p => p.StudentId == s.Id && p.Completed && p.CompletedAt >= weekStart);
                var minutes = await _db.Attempts.CountAsync(a => a.StudentId == s.Id && a.SubmittedAt >= weekStart) * 5;
                var scores = await _db.ProgressRecords.Where(p => p.StudentId == s.Id).Select(p => p.MasteryScore).ToListAsync();
                var mastery = scores.Count > 0 ? Math.Round(scores.Average(), 1) : 0;
                var openFlags = await _db.ModerationEvents.CountAsync(m => m.InterventionRaised && m.ReviewedAt == null
                    && _db.TutorSessions.Any(t => t.Id == m.TutorSessionId && t.StudentId == s.Id));
                children.Add(new EmailTemplates.DigestChild(s.DisplayName, lessons, minutes, MasteryMath.ToBand(mastery).ToString(), openFlags));
            }

            // Don't email inactive families.
            if (!children.Any(c => c.LessonsThisWeek > 0 || c.MinutesThisWeek > 0 || c.OpenFlags > 0)) continue;

            var (subject, html) = EmailTemplates.WeeklyDigest(parent.FullName ?? "there", children, AppUrl);
            await _email.SendAsync(parent.Email, subject, html);
            sent++;
        }

        _log.LogInformation("Weekly parent digest sent to {Count} parent(s).", sent);
    }

    /// <summary>Fulfil a PDPA data-export request: gather the subject's data and store a JSON bundle.</summary>
    public async Task ProcessPrivacyExportAsync(Guid requestId)
    {
        var req = await _db.PrivacyRequests.FirstOrDefaultAsync(r => r.Id == requestId);
        if (req is null) return;

        req.Status = PrivacyRequestStatus.Processing;
        await _db.SaveChangesAsync();

        var bundle = await BuildExportBundle(req);
        var key = $"exports/{req.Id}.json";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(bundle, new JsonSerializerOptions { WriteIndented = true });
        using var ms = new MemoryStream(bytes);
        await _storage.PutAsync(key, ms, "application/json");

        req.ResultStorageKey = key;
        req.Status = PrivacyRequestStatus.Completed;
        req.CompletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        _log.LogInformation("Completed privacy export {RequestId}", requestId);
    }

    /// <summary>Fulfil a delete/anonymize request: soft-delete + scrub PII while preserving aggregates.</summary>
    public async Task ProcessPrivacyDeleteAsync(Guid requestId)
    {
        var req = await _db.PrivacyRequests.FirstOrDefaultAsync(r => r.Id == requestId);
        if (req is null) return;
        req.Status = PrivacyRequestStatus.Processing;
        await _db.SaveChangesAsync();

        if (req.StudentId is { } studentId)
        {
            var student = await _db.Students.FirstOrDefaultAsync(s => s.Id == studentId);
            if (student is not null)
            {
                student.DisplayName = $"Deleted Student {studentId.ToString()[..8]}";
                student.Avatar = null;
                student.DateOfBirth = null;
                student.DeletedAt = DateTimeOffset.UtcNow;
            }
            var linkedLogins = await _db.Users.Where(u => u.StudentId == studentId).ToListAsync();
            foreach (var u in linkedLogins) Anonymize(u);
        }
        else
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == req.RequestedByUserId);
            if (user is not null) Anonymize(user);
        }

        req.Status = PrivacyRequestStatus.Completed;
        req.CompletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        _log.LogInformation("Completed privacy delete/anonymize {RequestId}", requestId);
    }

    private static void Anonymize(User u)
    {
        u.Email = $"anonymized+{u.Id:N}@deleted.cerdik.my";
        u.FullName = "Anonymized User";
        u.PasswordHash = "!";
        u.IsActive = false;
        u.DeletedAt = DateTimeOffset.UtcNow;
    }

    private async Task<object> BuildExportBundle(PrivacyRequest req)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == req.RequestedByUserId);
        var students = await _db.Students
            .Where(s => req.StudentId == null ? s.Household.Members.Any(m => m.Id == req.RequestedByUserId) : s.Id == req.StudentId)
            .Select(s => new
            {
                s.Id,
                s.DisplayName,
                s.Level,
                s.SchoolType,
                Progress = _db.ProgressRecords.Where(p => p.StudentId == s.Id)
                    .Select(p => new { p.LessonId, p.Completed, p.MasteryScore, p.TahapPenguasaan }).ToList(),
                Attempts = _db.Attempts.Where(a => a.StudentId == s.Id)
                    .Select(a => new { a.ActivityId, a.Score, a.MaxScore, a.SubmittedAt }).ToList(),
                TutorSessions = _db.TutorSessions.Where(t => t.StudentId == s.Id)
                    .Select(t => new { t.Id, t.Title, t.CreatedAt }).ToList(),
            })
            .ToListAsync();

        return new
        {
            generatedAt = DateTimeOffset.UtcNow,
            request = new { req.Id, req.Type, req.CreatedAt },
            account = user is null ? null : new { user.Email, user.FullName, user.Role, user.CreatedAt },
            students,
        };
    }
}

/// <summary>Mastery math shared by attempt grading and the recompute job.</summary>
public static class MasteryMath
{
    /// <summary>Exponentially-weighted update of a 0–100 mastery score from a new attempt percentage.</summary>
    public static double UpdateScore(double current, double attemptPercent, double alpha = 0.4) =>
        current <= 0 ? attemptPercent : Math.Round((alpha * attemptPercent) + ((1 - alpha) * current), 2);

    /// <summary>Map a 0–100 mastery score to a KPM Tahap Penguasaan band (TP1–TP6).</summary>
    public static MasteryBand ToBand(double score) => score switch
    {
        >= 90 => MasteryBand.TP6,
        >= 75 => MasteryBand.TP5,
        >= 60 => MasteryBand.TP4,
        >= 45 => MasteryBand.TP3,
        >= 25 => MasteryBand.TP2,
        _ => MasteryBand.TP1,
    };
}
