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
[Route("api/admin")]
public sealed class AdminContentController(
    LensoraDbContext db,
    ILogger<AdminContentController> logger) : ControllerBase
{
    [HttpGet("profile")]
    public async Task<ActionResult<AdminProfileResponse>> GetProfile()
    {
        try
        {
            var profile = await CurrentPhotographer();
            if (profile is null) return Forbid();

            return Ok(new AdminProfileResponse(profile.Name, profile.Slug, profile.Bio, profile.Location,
                profile.ProfileImageUrl, profile.ProfileImagePublicId, profile.HeroFocalX, profile.HeroFocalY));
        }
        catch (Exception exception)
        {
            LogOperationFailure(exception, "get profile");
            throw;
        }
    }

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile(UpdateProfileRequest request)
    {
        try
        {
            var profile = await CurrentPhotographer();
            if (profile is null) return Forbid();

            var slug = request.Slug.Trim().ToLowerInvariant();
            if (await db.Photographers.AnyAsync(x => x.Slug == slug && x.Id != profile.Id))
                return Conflict(new { message = "That public URL is already in use." });

            profile.Name = request.Name.Trim();
            profile.Slug = slug;
            profile.Bio = Clean(request.Bio);
            profile.Location = Clean(request.Location);
            profile.ProfileImageUrl = Clean(request.ProfileImageUrl);
            profile.ProfileImagePublicId = Clean(request.ProfileImagePublicId);
            profile.HeroFocalX = request.HeroFocalX;
            profile.HeroFocalY = request.HeroFocalY;
            await db.SaveChangesAsync();
            return NoContent();
        }
        catch (Exception exception)
        {
            LogOperationFailure(exception, "update profile");
            throw;
        }
    }

    [HttpGet("portfolio")]
    public async Task<ActionResult<IReadOnlyList<PortfolioItemResponse>>> ListPortfolio()
    {
        try
        {
            var profile = await CurrentPhotographer();
            if (profile is null) return Forbid();

            var items = await db.PortfolioItems.AsNoTracking()
                .Where(x => x.PhotographerId == profile.Id)
                .OrderBy(x => x.DisplayOrder)
                .Select(x => new PortfolioItemResponse(x.Id, x.ImageUrl, x.ImagePublicId, x.Title,
                    x.Category, x.DisplayOrder))
                .ToListAsync();
            return Ok(items);
        }
        catch (Exception exception)
        {
            LogOperationFailure(exception, "list portfolio");
            throw;
        }
    }

    [HttpPost("portfolio")]
    public async Task<ActionResult<PortfolioItemResponse>> AddPortfolio(CreatePortfolioItemRequest request)
    {
        try
        {
            var profile = await CurrentPhotographer();
            if (profile is null) return Forbid();

            if (await db.PortfolioItems.AnyAsync(x => x.PhotographerId == profile.Id && x.DisplayOrder == request.DisplayOrder))
                return Conflict(new { message = DisplayOrderConflict.Message });
            var item = new PortfolioItem
            {
                PhotographerId = profile.Id,
                ImageUrl = request.ImageUrl.Trim(),
                ImagePublicId = Clean(request.ImagePublicId),
                Title = Clean(request.Title),
                Category = Clean(request.Category),
                DisplayOrder = request.DisplayOrder
            };
            db.PortfolioItems.Add(item);
            await db.SaveChangesAsync();
            return Created($"api/admin/portfolio/{item.Id}", ToResponse(item));
        }
        catch (DbUpdateException exception) when (DisplayOrderConflict.IsUniqueIndexViolation(exception))
        {
            logger.LogWarning(exception, "Portfolio display order conflict. Order: {DisplayOrder}", request.DisplayOrder);
            return Conflict(new { message = DisplayOrderConflict.Message });
        }
        catch (Exception exception)
        {
            LogOperationFailure(exception, "add portfolio item");
            throw;
        }
    }

    [HttpPut("portfolio/{id:int}")]
    public async Task<IActionResult> UpdatePortfolio(int id, UpdatePortfolioItemRequest request)
    {
        try
        {
            var profile = await CurrentPhotographer();
            if (profile is null) return Forbid();

            var item = await db.PortfolioItems.SingleOrDefaultAsync(x => x.Id == id && x.PhotographerId == profile.Id);
            if (item is null) return NotFound();
            if (await db.PortfolioItems.AnyAsync(x => x.PhotographerId == profile.Id && x.Id != id && x.DisplayOrder == request.DisplayOrder))
                return Conflict(new { message = DisplayOrderConflict.Message });

            item.ImageUrl = request.ImageUrl.Trim();
            item.ImagePublicId = Clean(request.ImagePublicId);
            item.Title = Clean(request.Title);
            item.Category = Clean(request.Category);
            item.DisplayOrder = request.DisplayOrder;
            await db.SaveChangesAsync();
            return NoContent();
        }
        catch (DbUpdateException exception) when (DisplayOrderConflict.IsUniqueIndexViolation(exception))
        {
            logger.LogWarning(exception, "Portfolio display order conflict. ItemId: {ItemId}; Order: {DisplayOrder}", id, request.DisplayOrder);
            return Conflict(new { message = DisplayOrderConflict.Message });
        }
        catch (Exception exception)
        {
            LogOperationFailure(exception, "update portfolio item", id);
            throw;
        }
    }

    [HttpDelete("portfolio/{id:int}")]
    public async Task<IActionResult> DeletePortfolio(int id)
    {
        try
        {
            var profile = await CurrentPhotographer();
            if (profile is null) return Forbid();

            var item = await db.PortfolioItems.SingleOrDefaultAsync(x => x.Id == id && x.PhotographerId == profile.Id);
            if (item is null) return NotFound();

            db.PortfolioItems.Remove(item);
            await db.SaveChangesAsync();
            return NoContent();
        }
        catch (Exception exception)
        {
            LogOperationFailure(exception, "delete portfolio item", id);
            throw;
        }
    }

    [HttpGet("gear")]
    public async Task<ActionResult<IReadOnlyList<GearItemResponse>>> ListGear()
    {
        try
        {
            var profile = await CurrentPhotographer();
            if (profile is null) return Forbid();

            var items = await db.GearItems.AsNoTracking()
                .Where(x => x.PhotographerId == profile.Id)
                .OrderBy(x => x.DisplayOrder)
                .Select(x => new GearItemResponse(x.Id, x.Name, x.Category, x.Brand, x.Model,
                    x.Description, x.ImageUrl, x.ImagePublicId, x.IsFeatured, x.DisplayOrder))
                .ToListAsync();
            return Ok(items);
        }
        catch (Exception exception)
        {
            LogOperationFailure(exception, "list gear");
            throw;
        }
    }

    [HttpPost("gear")]
    public async Task<ActionResult<GearItemResponse>> AddGear(CreateGearItemRequest request)
    {
        try
        {
            var profile = await CurrentPhotographer();
            if (profile is null) return Forbid();

            if (await db.GearItems.AnyAsync(x => x.PhotographerId == profile.Id && x.DisplayOrder == request.DisplayOrder))
                return Conflict(new { message = DisplayOrderConflict.Message });
            var item = new GearItem
            {
                PhotographerId = profile.Id,
                Name = request.Name.Trim(),
                Category = request.Category.Trim(),
                Brand = request.Brand.Trim(),
                Model = request.Model.Trim(),
                Description = Clean(request.Description),
                ImageUrl = Clean(request.ImageUrl),
                ImagePublicId = Clean(request.ImagePublicId),
                IsFeatured = request.IsFeatured,
                DisplayOrder = request.DisplayOrder
            };
            db.GearItems.Add(item);
            await db.SaveChangesAsync();
            return Created($"api/admin/gear/{item.Id}", ToResponse(item));
        }
        catch (DbUpdateException exception) when (DisplayOrderConflict.IsUniqueIndexViolation(exception))
        {
            logger.LogWarning(exception, "Gear display order conflict. Order: {DisplayOrder}", request.DisplayOrder);
            return Conflict(new { message = DisplayOrderConflict.Message });
        }
        catch (Exception exception)
        {
            LogOperationFailure(exception, "add gear item");
            throw;
        }
    }

    [HttpPut("gear/{id:int}")]
    public async Task<IActionResult> UpdateGear(int id, UpdateGearItemRequest request)
    {
        try
        {
            var profile = await CurrentPhotographer();
            if (profile is null) return Forbid();

            var item = await db.GearItems.SingleOrDefaultAsync(x => x.Id == id && x.PhotographerId == profile.Id);
            if (item is null) return NotFound();
            if (await db.GearItems.AnyAsync(x => x.PhotographerId == profile.Id && x.Id != id && x.DisplayOrder == request.DisplayOrder))
                return Conflict(new { message = DisplayOrderConflict.Message });

            item.Name = request.Name.Trim();
            item.Category = request.Category.Trim();
            item.Brand = request.Brand.Trim();
            item.Model = request.Model.Trim();
            item.Description = Clean(request.Description);
            item.ImageUrl = Clean(request.ImageUrl);
            item.ImagePublicId = Clean(request.ImagePublicId);
            item.IsFeatured = request.IsFeatured;
            item.DisplayOrder = request.DisplayOrder;
            await db.SaveChangesAsync();
            return NoContent();
        }
        catch (DbUpdateException exception) when (DisplayOrderConflict.IsUniqueIndexViolation(exception))
        {
            logger.LogWarning(exception, "Gear display order conflict. ItemId: {ItemId}; Order: {DisplayOrder}", id, request.DisplayOrder);
            return Conflict(new { message = DisplayOrderConflict.Message });
        }
        catch (Exception exception)
        {
            LogOperationFailure(exception, "update gear item", id);
            throw;
        }
    }

    [HttpDelete("gear/{id:int}")]
    public async Task<IActionResult> DeleteGear(int id)
    {
        try
        {
            var profile = await CurrentPhotographer();
            if (profile is null) return Forbid();

            var item = await db.GearItems.SingleOrDefaultAsync(x => x.Id == id && x.PhotographerId == profile.Id);
            if (item is null) return NotFound();

            db.GearItems.Remove(item);
            await db.SaveChangesAsync();
            return NoContent();
        }
        catch (Exception exception)
        {
            LogOperationFailure(exception, "delete gear item", id);
            throw;
        }
    }

    private async Task<Photographer?> CurrentPhotographer()
    {
        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(subject, out var userId)
            ? await db.Photographers.SingleOrDefaultAsync(x => x.UserId == userId)
            : null;
    }

    private void LogOperationFailure(Exception exception, string operation, int? itemId = null)
    {
        logger.LogError(exception,
            "Admin content operation failed. Operation: {Operation}; ItemId: {ItemId}; TraceId: {TraceId}",
            operation, itemId, HttpContext.TraceIdentifier);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static PortfolioItemResponse ToResponse(PortfolioItem item) =>
        new(item.Id, item.ImageUrl, item.ImagePublicId, item.Title, item.Category, item.DisplayOrder);

    private static GearItemResponse ToResponse(GearItem item) =>
        new(item.Id, item.Name, item.Category, item.Brand, item.Model, item.Description, item.ImageUrl,
            item.ImagePublicId, item.IsFeatured, item.DisplayOrder);
}
