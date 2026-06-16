using Cerdik.Application.Abstractions;
using Cerdik.Application.Dtos;
using Cerdik.Application.Features;
using Cerdik.Domain;
using Cerdik.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cerdik.Api.Controllers;

[ApiController]
[Authorize]
public sealed class MeController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _current;
    private readonly IFeatureFlags _flags;

    public MeController(AppDbContext db, ICurrentUser current, IFeatureFlags flags)
    {
        _db = db;
        _current = current;
        _flags = flags;
    }

    [HttpGet("/me")]
    public async Task<ActionResult<MeResponse>> Me(CancellationToken ct)
    {
        var userId = _current.UserId ?? throw ApiException.Unauthorized();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw ApiException.Unauthorized();

        // Parents see all household students; a student sees only themselves.
        var studentsQuery = user.Role == UserRole.Student && user.StudentId is { } sid
            ? _db.Students.Where(s => s.Id == sid)
            : _db.Students.Where(s => s.HouseholdId == user.HouseholdId && s.DeletedAt == null);

        var studentEntities = await studentsQuery.OrderBy(s => s.DisplayName).AsNoTracking().ToListAsync(ct);
        var students = studentEntities.Select(s => s.ToSummary()).ToList();

        return Ok(new MeResponse(user.ToDto(), students, _flags.Snapshot()));
    }
}
