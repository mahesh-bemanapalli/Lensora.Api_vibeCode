using System.Security.Claims;
using Lensora.Api.Contracts;
using Lensora.Api.Domain;
using Lensora.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lensora.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/admin/packages")]
public sealed class AdminPackagesController(LensoraDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PackageResponse>>> List()
    {
        var photographerId = await CurrentPhotographerId();
        if (photographerId is null) return Forbid();
        var packages = await db.Packages.AsNoTracking()
            .Where(x => x.PhotographerId == photographerId)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id)
            .Select(x => ToResponse(x)).ToListAsync();
        return Ok(packages);
    }

    [HttpPost]
    public async Task<ActionResult<PackageResponse>> Create(UpsertPackageRequest request)
    {
        var photographerId = await CurrentPhotographerId();
        if (photographerId is null) return Forbid();
        if (await db.Packages.AnyAsync(x => x.PhotographerId == photographerId && x.DisplayOrder == request.DisplayOrder))
            return Conflict(new { message = DisplayOrderConflict.Message });
        var package = new Package
        {
            PhotographerId = photographerId.Value,
            Name = request.Name.Trim(),
            Description = request.Description.Trim(),
            ImageUrl = request.ImageUrl.Trim(),
            Currency = request.Currency
        };
        Apply(package, request);
        db.Packages.Add(package);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (DisplayOrderConflict.IsUniqueIndexViolation(exception))
        {
            return Conflict(new { message = DisplayOrderConflict.Message });
        }
        return Created($"/api/admin/packages/{package.Id}", ToResponse(package));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpsertPackageRequest request)
    {
        var photographerId = await CurrentPhotographerId();
        if (photographerId is null) return Forbid();
        var package = await db.Packages.SingleOrDefaultAsync(x => x.Id == id && x.PhotographerId == photographerId);
        if (package is null) return NotFound();
        if (await db.Packages.AnyAsync(x => x.PhotographerId == photographerId && x.Id != id && x.DisplayOrder == request.DisplayOrder))
            return Conflict(new { message = DisplayOrderConflict.Message });
        Apply(package, request);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (DisplayOrderConflict.IsUniqueIndexViolation(exception))
        {
            return Conflict(new { message = DisplayOrderConflict.Message });
        }
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var photographerId = await CurrentPhotographerId();
        if (photographerId is null) return Forbid();
        var package = await db.Packages.SingleOrDefaultAsync(x => x.Id == id && x.PhotographerId == photographerId);
        if (package is null) return NotFound();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Bookings.Where(x => x.PackageId == id).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.PackageId, (int?)null));
        db.Packages.Remove(package);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return NoContent();
    }

    private async Task<int?> CurrentPhotographerId()
    {
        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(subject, out var userId)
            ? await db.Photographers.Where(x => x.UserId == userId).Select(x => (int?)x.Id).SingleOrDefaultAsync()
            : null;
    }

    private static void Apply(Package package, UpsertPackageRequest request)
    {
        package.Name = request.Name.Trim();
        package.Description = request.Description.Trim();
        package.ImageUrl = request.ImageUrl.Trim();
        package.ImagePublicId = string.IsNullOrWhiteSpace(request.ImagePublicId) ? null : request.ImagePublicId.Trim();
        package.Price = request.Price;
        package.Currency = request.Currency;
        package.CoverageHours = request.CoverageHours;
        package.Deliverables = string.IsNullOrWhiteSpace(request.Deliverables) ? null : request.Deliverables.Trim();
        package.IsPublished = request.IsPublished;
        package.DisplayOrder = request.DisplayOrder;
    }

    private static PackageResponse ToResponse(Package x) =>
        new(x.Id, x.Name, x.Description, x.ImageUrl, x.ImagePublicId, x.Price, x.Currency,
            x.CoverageHours, x.Deliverables, x.IsPublished, x.DisplayOrder);
}
