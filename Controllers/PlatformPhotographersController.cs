using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Lensora.Api.Domain;
using Lensora.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lensora.Api.Controllers;

[ApiController, Authorize(Roles = "SuperAdmin"), Route("api/platform/photographers")]
public sealed class PlatformPhotographersController(LensoraDbContext db) : ControllerBase
{
    // Check the current database role as well, so demoted accounts cannot use stale tokens.
    private async Task<int?> SuperAdminId()
    {
        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(subject, out var id) && await db.Users.AnyAsync(x => x.Id == id && x.Role == "SuperAdmin") ? id : null;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        if (await SuperAdminId() is null) return Forbid();
        return Ok(await db.Photographers.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.Slug, x.TemplateKey }).ToListAsync());
    }

    [HttpPut("{slug}/design")]
    public async Task<IActionResult> Assign(string slug, AssignDesignRequest request)
    {
        var actorId = await SuperAdminId();
        if (actorId is null) return Forbid();
        // Serialize assignments so the audit's previous design always matches the saved value.
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var profile = await db.Photographers.SingleOrDefaultAsync(x => x.Slug == slug);
        if (profile is null) return NotFound();
        if (profile.TemplateKey != request.TemplateKey)
        {
            db.TemplateAssignments.Add(new TemplateAssignment
            {
                PhotographerId = profile.Id, AssignedByUserId = actorId.Value,
                PreviousTemplateKey = profile.TemplateKey, TemplateKey = request.TemplateKey,
                AssignedUtc = DateTime.UtcNow
            });
            profile.TemplateKey = request.TemplateKey;
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync();
        return Ok(new { profile.Slug, profile.TemplateKey });
    }

    [HttpGet("{slug}/preview")]
    public async Task<IActionResult> Preview(string slug)
    {
        if (await SuperAdminId() is null) return Forbid();
        var result = await new PublicProfilesController(db).Get(slug);
        return result.Result!;
    }
}

public sealed record AssignDesignRequest([Required, RegularExpression("^(editorial|minimal-gallery)$")] string TemplateKey);
