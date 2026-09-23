using Lensora.Api.Contracts;
using Lensora.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace Lensora.Api.Controllers;

[ApiController, Route("api/photographers")]
public sealed class PublicProfilesController(LensoraDbContext db) : ControllerBase
{
    [HttpGet("{slug}")]
    public async Task<ActionResult<PublicProfileResponse>> Get(string slug)
    {
        var photographer = await db.Photographers.AsNoTracking().Include(x => x.PortfolioItems).Include(x => x.GearItems).Include(x => x.Packages).SingleOrDefaultAsync(x => x.Slug == slug);
        if (photographer is null) return NotFound();
        return Ok(new PublicProfileResponse(photographer.Name, photographer.Slug, photographer.Bio, photographer.Location, photographer.ProfileImageUrl, photographer.PortfolioItems.OrderBy(x => x.DisplayOrder).Select(x => new PortfolioItemResponse(x.Id, x.ImageUrl, x.ImagePublicId, x.Title, x.Category, x.DisplayOrder)).ToList(), photographer.GearItems.OrderBy(x => x.DisplayOrder).Select(x => new GearItemResponse(x.Id, x.Name, x.Category, x.Brand, x.Model, x.Description, x.ImageUrl, x.ImagePublicId, x.IsFeatured, x.DisplayOrder)).ToList(), photographer.Packages.Where(x => x.IsPublished).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).Select(x => new PackageResponse(x.Id, x.Name, x.Description, x.ImageUrl, x.ImagePublicId, x.Price, x.Currency, x.CoverageHours, x.Deliverables, x.IsPublished, x.DisplayOrder)).ToList()));
    }
}
