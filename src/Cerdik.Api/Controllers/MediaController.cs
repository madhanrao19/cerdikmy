using Cerdik.Application.Abstractions;
using Cerdik.Application.Dtos;
using Cerdik.Domain.Entities;
using Cerdik.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cerdik.Api.Controllers;

/// <summary>Lesson media library: content admins upload images/video/audio to object storage,
/// and the records are surfaced (with short-lived presigned URLs) for use in lesson blocks.</summary>
[ApiController]
[Route("admin/media")]
[Authorize(Roles = "Admin,ContentAdmin")]
public sealed class MediaController : ControllerBase
{
    private const long MaxBytes = 25 * 1024 * 1024; // 25 MB

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/gif", "image/webp", "image/svg+xml",
        "video/mp4", "video/webm", "audio/mpeg", "audio/mp4", "audio/ogg", "audio/wav",
    };

    private readonly AppDbContext _db;
    private readonly IStorageService _storage;
    private readonly ICurrentUser _current;

    public MediaController(AppDbContext db, IStorageService storage, ICurrentUser current)
    {
        _db = db;
        _storage = storage;
        _current = current;
    }

    [HttpPost]
    [RequestSizeLimit(MaxBytes + 1_048_576)]
    public async Task<ActionResult<MediaAssetDto>> Upload([FromForm] IFormFile? file, [FromForm] string? altText, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            throw ApiException.BadRequest("A file is required.", "no_file");
        }
        if (file.Length > MaxBytes)
        {
            throw ApiException.BadRequest("File is too large (max 25 MB).", "too_large");
        }
        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
        if (!AllowedContentTypes.Contains(contentType))
        {
            throw ApiException.BadRequest($"Unsupported file type '{contentType}'.", "unsupported_type");
        }

        var orgId = _current.OrganizationId ?? await _db.Organizations.Select(o => o.Id).FirstAsync(ct);
        var safeName = Path.GetFileName(file.FileName);
        var key = $"media/{Guid.CreateVersion7()}/{safeName}";

        await using (var stream = file.OpenReadStream())
        {
            await _storage.PutAsync(key, stream, contentType, ct);
        }

        var asset = new MediaAsset
        {
            OrganizationId = orgId,
            StorageKey = key,
            FileName = safeName,
            ContentType = contentType,
            SizeBytes = file.Length,
            AltText = altText,
            UploadedByUserId = _current.UserId ?? Guid.Empty,
        };
        _db.MediaAssets.Add(asset);
        _db.AuditLogs.Add(new AuditLog
        {
            OrganizationId = orgId,
            ActorUserId = _current.UserId,
            ActorEmail = _current.Email,
            Action = "media.upload",
            EntityType = "MediaAsset",
            EntityId = asset.Id.ToString(),
        });
        await _db.SaveChangesAsync(ct);

        var url = await _storage.GetPresignedUrlAsync(key, TimeSpan.FromHours(6), ct: ct);
        return Ok(new MediaAssetDto(asset.Id, url, asset.ContentType, asset.AltText, asset.DurationSeconds));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MediaAssetDto>>> List(CancellationToken ct)
    {
        var orgId = _current.OrganizationId;
        var assets = await _db.MediaAssets.AsNoTracking()
            .Where(m => m.OrganizationId == orgId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        var result = new List<MediaAssetDto>(assets.Count);
        foreach (var m in assets)
        {
            var url = await _storage.GetPresignedUrlAsync(m.StorageKey, TimeSpan.FromHours(6), ct: ct);
            result.Add(new MediaAssetDto(m.Id, url, m.ContentType, m.AltText, m.DurationSeconds));
        }
        return Ok(result);
    }
}
