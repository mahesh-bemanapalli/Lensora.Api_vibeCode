using System.Security.Claims;
using Lensora.Api.Contracts;
using Lensora.Api.Domain;
using Lensora.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
namespace Lensora.Api.Controllers;

[ApiController, Route("api")]
public sealed class BookingsController(LensoraDbContext db) : ControllerBase
{
    private const string DuplicateBookingMessage = "This email address has already sent a booking inquiry to this photographer.";

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
        var normalizedEmail = request.ClientEmail.Trim().ToLowerInvariant();
        if (await db.Bookings.AnyAsync(x => x.PhotographerId == photographer.Id && x.ClientEmail.Trim().ToLower() == normalizedEmail))
            return Conflict(new { message = DuplicateBookingMessage });
        if (request.PackageId is int packageId && !await db.Packages.AnyAsync(x => x.Id == packageId && x.PhotographerId == photographer.Id && x.IsPublished))
            return BadRequest(new { message = "The selected package is unavailable." });
        db.Bookings.Add(new Booking
        {
            PhotographerId = photographer.Id,
            PackageId = request.PackageId,
            ClientName = request.ClientName.Trim(),
            ClientEmail = normalizedEmail,
            EventDate = request.EventDate,
            Message = request.Message?.Trim(),
            Status = "Pending",
            CreatedUtc = DateTime.UtcNow
        }
        );
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (exception.GetBaseException() is SqlException sql && sql.Number is 2601 or 2627)
        {
            return Conflict(new { message = DuplicateBookingMessage });
        }
        return StatusCode(StatusCodes.Status201Created);
    }
    [Authorize, HttpGet("admin/bookings")]
    public async Task<ActionResult<IReadOnlyList<BookingResponse>>> List()
    {
        var photographerId = await GetPhotographerId();

        if (photographerId is null) return Forbid();
        return Ok(await db.Bookings.AsNoTracking().Where(x => x.PhotographerId == photographerId).OrderByDescending(x => x.CreatedUtc).Select(x => new BookingResponse(x.Id, x.ClientName, x.ClientEmail, x.EventDate, x.Message, x.Status, x.CreatedUtc, x.PackageId, x.Package != null ? x.Package.Name : null)).ToListAsync());
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
