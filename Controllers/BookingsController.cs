using System.Security.Claims;
using Lensora.Api.Contracts;
using Lensora.Api.Domain;
using Lensora.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace Lensora.Api.Controllers;

[ApiController, Route("api")]
public sealed class BookingsController(LensoraDbContext db) : ControllerBase
{
    [HttpPost("photographers/{slug}/bookings")]
    public async Task<IActionResult> Create(string slug, CreateBookingRequest request)
    {
        if (request.EventDate < DateOnly.FromDateTime(DateTime.UtcNow.Date)) return BadRequest(new
        {
            message = "Event date cannot be in the past."
        }
        );
        var photographer = await db.Photographers.SingleOrDefaultAsync(x => x.Slug == slug);
        if (photographer is null) return NotFound();
        db.Bookings.Add(new Booking
        {
            PhotographerId = photographer.Id,
            ClientName = request.ClientName.Trim(),
            ClientEmail = request.ClientEmail.Trim().ToLowerInvariant(),
            EventDate = request.EventDate,
            Message = request.Message?.Trim(),
            Status = "Pending",
            CreatedUtc = DateTime.UtcNow
        }
        );
        await db.SaveChangesAsync();
        return StatusCode(StatusCodes.Status201Created);
    }
    [Authorize, HttpGet("admin/bookings")]
    public async Task<ActionResult<IReadOnlyList<BookingResponse>>> List()
    {
        var photographerId = await GetPhotographerId();

        if (photographerId is null) return Forbid();
        return Ok(await db.Bookings.AsNoTracking().Where(x => x.PhotographerId == photographerId).OrderByDescending(x => x.CreatedUtc).Select(x => new BookingResponse(x.Id, x.ClientName, x.ClientEmail, x.EventDate, x.Message, x.Status, x.CreatedUtc)).ToListAsync());
    }
    [Authorize, HttpPatch("admin/bookings/{id}/status")]
    public async Task<IActionResult> UpdateStatus(int id, UpdateBookingStatusRequest request)
    {
        var photographerId = await GetPhotographerId();
        if (photographerId is null) return Forbid();
        var booking = await db.Bookings.SingleOrDefaultAsync(x => x.Id == id && x.PhotographerId == photographerId);
        if (booking is null) return NotFound();
        booking.Status = request.Status;
        await db.SaveChangesAsync();
        return NoContent();
    }
    private async Task<int?> GetPhotographerId()
    {
        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(subject, out var userId) ? await db.Photographers.Where(x => x.UserId == userId).Select(x => (int?)x.Id).SingleOrDefaultAsync() : null;
    }
}
