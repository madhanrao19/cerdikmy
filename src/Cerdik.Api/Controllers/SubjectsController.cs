using Cerdik.Application.Dtos;
using Cerdik.Domain;
using Cerdik.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cerdik.Api.Controllers;

[ApiController]
[Authorize]
public sealed class SubjectsController : ControllerBase
{
    private readonly AppDbContext _db;

    public SubjectsController(AppDbContext db) => _db = db;

    /// <summary>List subjects, filtered by curriculum context. Variants are filtered to the requested
    /// school_type / language / dlp_mode where provided.</summary>
    [HttpGet("/subjects")]
    public async Task<ActionResult<IReadOnlyList<SubjectDto>>> Subjects(
        [FromQuery] string? curriculumVersionCode,
        [FromQuery] Level? level,
        [FromQuery] SchoolType? schoolType,
        [FromQuery] Language? language,
        [FromQuery] DlpMode? dlpMode,
        CancellationToken ct)
    {
        var query = _db.Subjects.AsNoTracking()
            .Include(s => s.CurriculumVersion)
            .Include(s => s.Variants)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(curriculumVersionCode))
            query = query.Where(s => s.CurriculumVersion.Code == curriculumVersionCode);
        if (level is { } lvl) query = query.Where(s => s.Level == lvl);

        var subjects = await query.OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToListAsync(ct);

        var dtos = subjects.Select(s =>
        {
            var variants = s.Variants.AsEnumerable();
            if (schoolType is { } st) variants = variants.Where(v => v.SchoolType == st);
            if (language is { } lang) variants = variants.Where(v => v.Language == lang);
            if (dlpMode is { } dlp) variants = variants.Where(v => v.DlpMode == dlp);

            return new SubjectDto(
                s.Id, s.CurriculumVersionId, s.Code, s.Name, s.GradeBand, s.Level, s.SortOrder,
                variants.Select(v => new SubjectVariantDto(v.Id, v.SchoolType, v.Language, v.DlpMode, v.State, v.Label)).ToList());
        }).ToList();

        return Ok(dtos);
    }

    [HttpGet("/subjects/{id:guid}/standards")]
    public async Task<ActionResult<IReadOnlyList<LearningStandardDto>>> Standards(Guid id, CancellationToken ct)
    {
        if (!await _db.Subjects.AnyAsync(s => s.Id == id, ct)) throw ApiException.NotFound("Subject");
        var standards = await _db.LearningStandards.AsNoTracking()
            .Where(s => s.SubjectId == id)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Code)
            .ToListAsync(ct);
        return Ok(standards.Select(s => s.ToDto()).ToList());
    }
}
