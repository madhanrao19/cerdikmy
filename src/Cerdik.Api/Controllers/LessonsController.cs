using Cerdik.Application.Abstractions;
using Cerdik.Application.Dtos;
using Cerdik.Domain;
using Cerdik.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cerdik.Api.Controllers;

[ApiController]
[Authorize]
public sealed class LessonsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IStorageService _storage;

    public LessonsController(AppDbContext db, IStorageService storage)
    {
        _db = db;
        _storage = storage;
    }

    [HttpGet("/lessons/{id:guid}")]
    public async Task<ActionResult<LessonDto>> Get(Guid id, CancellationToken ct)
    {
        var lesson = await _db.Lessons.AsNoTracking()
            .Include(l => l.Blocks).ThenInclude(b => b.MediaAsset)
            .Include(l => l.Activities)
            .FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw ApiException.NotFound("Lesson");

        var blocks = new List<LessonBlockDto>();
        foreach (var b in lesson.Blocks.OrderBy(b => b.SortOrder))
        {
            MediaAssetDto? media = null;
            if (b.MediaAsset is { } m)
            {
                var url = await _storage.GetPresignedUrlAsync(m.StorageKey, TimeSpan.FromHours(1), ct: ct);
                media = new MediaAssetDto(m.Id, url, m.ContentType, m.AltText, m.DurationSeconds);
            }
            blocks.Add(new LessonBlockDto(b.Id, b.Type, b.SortOrder, b.Markdown, media, b.ConfigJson));
        }

        return Ok(lesson.ToDto(blocks));
    }
}
