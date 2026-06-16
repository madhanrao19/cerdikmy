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
public sealed class CurriculumController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _current;

    public CurriculumController(AppDbContext db, ICurrentUser current)
    {
        _db = db;
        _current = current;
    }

    [HttpGet("/curriculum/versions")]
    public async Task<ActionResult<IReadOnlyList<CurriculumVersionDto>>> Versions([FromQuery] Level? level, CancellationToken ct)
    {
        var query = _db.CurriculumVersions.AsNoTracking().Where(c => c.IsActive);
        if (level is { } lvl) query = query.Where(c => c.Level == lvl);
        var items = await query.OrderBy(c => c.Level).ThenBy(c => c.Code).ToListAsync(ct);
        return Ok(items.Select(c => c.ToDto()).ToList());
    }

    [HttpGet("/school-profiles")]
    public async Task<ActionResult<IReadOnlyList<SchoolProfileDto>>> SchoolProfiles(CancellationToken ct)
    {
        var orgId = _current.OrganizationId;
        var items = await _db.SchoolProfiles.AsNoTracking()
            .Where(s => s.OrganizationId == orgId)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);
        return Ok(items.Select(s => s.ToDto()).ToList());
    }
}
