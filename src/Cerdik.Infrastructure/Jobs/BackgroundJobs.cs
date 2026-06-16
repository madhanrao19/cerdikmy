using System.Text.Json;
using Cerdik.Application.Abstractions;
using Cerdik.Domain;
using Cerdik.Domain.Entities;
using Cerdik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cerdik.Infrastructure.Jobs;

/// <summary>Hangfire-invoked jobs. Each method is enqueued by name from the API/worker, so they take
/// simple serializable arguments and resolve their own scoped dependencies via constructor injection.</summary>
public sealed class BackgroundJobs
{
    private readonly AppDbContext _db;
    private readonly ContentIndexer _indexer;
    private readonly IStorageService _storage;
    private readonly ILogger<BackgroundJobs> _log;

    public BackgroundJobs(AppDbContext db, ContentIndexer indexer, IStorageService storage, ILogger<BackgroundJobs> log)
    {
        _db = db;
        _indexer = indexer;
        _storage = storage;
        _log = log;
    }

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
