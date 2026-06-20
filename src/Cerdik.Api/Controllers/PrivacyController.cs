using Cerdik.Application.Abstractions;
using Cerdik.Application.Dtos;
using Cerdik.Application.Email;
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
[Authorize]
[Route("privacy")]
public sealed class PrivacyController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _current;
    private readonly IStorageService _storage;
    private readonly IEmailSender _email;

    public PrivacyController(AppDbContext db, ICurrentUser current, IStorageService storage, IEmailSender email)
    {
        _db = db;
        _current = current;
        _storage = storage;
        _email = email;
    }

    [HttpPost("export")]
    public async Task<ActionResult<PrivacyRequestDto>> Export([FromBody] PrivacyExportRequest req, CancellationToken ct)
    {
        await EnsureStudentAccessIfProvided(req.StudentId, ct);
        var request = await Create(PrivacyRequestType.Export, req.StudentId, null, ct);
        BackgroundJob.Enqueue<BackgroundJobs>(j => j.ProcessPrivacyExportAsync(request.Id));
        await NotifyAsync("export", ct);
        return Ok(await ToDto(request, ct));
    }

    [HttpPost("delete-request")]
    public async Task<ActionResult<PrivacyRequestDto>> DeleteRequest([FromBody] PrivacyDeleteRequest req, CancellationToken ct)
    {
        await EnsureStudentAccessIfProvided(req.StudentId, ct);
        var request = await Create(PrivacyRequestType.Delete, req.StudentId, req.Reason, ct);
        BackgroundJob.Enqueue<BackgroundJobs>(j => j.ProcessPrivacyDeleteAsync(request.Id));
        await NotifyAsync("delete", ct);
        return Ok(await ToDto(request, ct));
    }

    private async Task<PrivacyRequest> Create(PrivacyRequestType type, Guid? studentId, string? reason, CancellationToken ct)
    {
        var request = new PrivacyRequest
        {
            RequestedByUserId = _current.UserId ?? throw ApiException.Unauthorized(),
            StudentId = studentId,
            Type = type,
            Reason = reason,
            Status = PrivacyRequestStatus.Received,
        };
        _db.PrivacyRequests.Add(request);
        _db.AuditLogs.Add(new AuditLog
        {
            OrganizationId = _current.OrganizationId,
            ActorUserId = _current.UserId,
            ActorEmail = _current.Email,
            Action = type == PrivacyRequestType.Export ? "privacy.export.request" : "privacy.delete.request",
            EntityType = "PrivacyRequest",
            EntityId = request.Id.ToString(),
        });
        await _db.SaveChangesAsync(ct);
        return request;
    }

    private async Task<PrivacyRequestDto> ToDto(PrivacyRequest r, CancellationToken ct)
    {
        string? url = null;
        if (r.ResultStorageKey is not null)
        {
            url = await _storage.GetPresignedUrlAsync(r.ResultStorageKey, TimeSpan.FromHours(24), ct: ct);
        }
        return new PrivacyRequestDto(r.Id, r.Type, r.Status, r.CreatedAt, r.CompletedAt, url);
    }

    private async Task NotifyAsync(string kind, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_current.Email)) return;
        var (subject, html) = EmailTemplates.PrivacyRequestReceived(kind);
        await _email.SendAsync(_current.Email, subject, html, ct);
    }

    private async Task EnsureStudentAccessIfProvided(Guid? studentId, CancellationToken ct)
    {
        if (studentId is not { } sid) return;
        if (_current.IsInRole(UserRole.Admin)) return;
        var ok = await _db.StudentGuardians.AnyAsync(g => g.StudentId == sid && g.GuardianUserId == _current.UserId, ct);
        if (!ok) throw ApiException.Forbidden("You don't guardian this student.");
    }
}
