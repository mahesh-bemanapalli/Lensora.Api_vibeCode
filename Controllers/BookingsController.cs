using System.Security.Claims;
using Lensora.Api.Contracts;
using Lensora.Api.Domain;
using Lensora.Api.Infrastructure;
using Lensora.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
namespace Lensora.Api.Controllers;

[ApiController, Route("api")]
public sealed class BookingsController(LensoraDbContext db) : ControllerBase
{
    private const string DuplicateBookingMessage = "An inquiry for this event date has already been sent using this email address.";

    [HttpPost("photographers/{slug}/bookings")]
    public async Task<IActionResult> Create(string slug, CreateBookingRequest request)
    {
        if (request.EventDate < DateOnly.FromDateTime(DateTime.UtcNow.Date)) return BadRequest(new
        {
            message = "Event date cannot be in the past."
        }
        );
        if (request.WhatsAppOptIn && string.IsNullOrWhiteSpace(request.ClientPhone))
            return BadRequest(new { message = "Enter a WhatsApp number to receive WhatsApp updates." });
        var photographer = await db.Photographers.Include(x => x.User).SingleOrDefaultAsync(x => x.Slug == slug);
        if (photographer is null) return NotFound();
        var normalizedEmail = request.ClientEmail.Trim().ToLowerInvariant();
        if (await db.Bookings.AnyAsync(x => x.PhotographerId == photographer.Id && x.EventDate == request.EventDate && x.ClientEmail.Trim().ToLower() == normalizedEmail))
            return Conflict(new { message = DuplicateBookingMessage });
        if (request.PackageId is int packageId && !await db.Packages.AnyAsync(x => x.Id == packageId && x.PhotographerId == photographer.Id && x.IsPublished))
            return BadRequest(new { message = "The selected package is unavailable." });
        var booking = new Booking
        {
            PhotographerId = photographer.Id,
            PackageId = request.PackageId,
            ClientName = request.ClientName.Trim(),
            ClientEmail = normalizedEmail,
            ClientPhone = request.WhatsAppOptIn ? request.ClientPhone?.Trim() : null,
            WhatsAppOptIn = request.WhatsAppOptIn,
            WhatsAppOptInUtc = request.WhatsAppOptIn ? DateTime.UtcNow : null,
            WhatsAppConsentVersion = request.WhatsAppOptIn ? "booking-updates-v1" : null,
            EventDate = request.EventDate,
            Message = request.Message?.Trim(),
            Status = "Pending",
            CreatedUtc = DateTime.UtcNow
        };
        BookingNotificationPlanner.Queue(booking, "InquiryReceived", photographer.User?.Email);
        db.Bookings.Add(booking);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (exception.GetBaseException() is SqlException sql && sql.Number is 2601 or 2627)
        {
            return Conflict(new { message = DuplicateBookingMessage });
        }
        return StatusCode(StatusCodes.Status201Created, new { bookingId = booking.Id, message = "Your inquiry was received. Confirmation messages are being processed." });
    }
    [Authorize, HttpGet("admin/bookings")]
    public async Task<ActionResult<IReadOnlyList<BookingResponse>>> List()
    {
        var photographerId = await GetPhotographerId();
        try {

            if (photographerId is null) return Forbid();
            var bookings = await db.Bookings.AsNoTracking()
                .Where(x => x.PhotographerId == photographerId)
                .Include(x => x.Package).Include(x => x.Notifications)
                .OrderByDescending(x => x.CreatedUtc).ToListAsync();
            return Ok(bookings.Select(x => new BookingResponse(
                x.Id, x.ClientName, x.ClientEmail, x.ClientPhone, x.WhatsAppOptIn,
                x.EventDate, x.Message, x.Status, x.CreatedUtc, x.PackageId, x.Package?.Name,
                x.InternalNotes, x.FollowUpUtc.HasValue
                    ? DateTime.SpecifyKind(x.FollowUpUtc.Value, DateTimeKind.Utc)
                    : null,
                x.Notifications.OrderBy(n => n.CreatedUtc).Select(n => new NotificationResponse(
                    n.Id, n.EventType, n.Channel, n.Status, n.Attempts, n.CreatedUtc, n.SentUtc, n.LastError)).ToList(),
                x.AgreedAmount, x.AmountReceived, x.AgreedAmount - x.AmountReceived, x.Currency
            )).ToList());
        }
        catch (Exception ex)
        {
            return null!;
        }
      

    }
    [Authorize, HttpPatch("admin/bookings/{id}/status")]
    public async Task<IActionResult> UpdateStatus(int id, UpdateBookingStatusRequest request)
    {
        var photographerId = await GetPhotographerId();
        if (photographerId is null) return Forbid();
        var booking = await db.Bookings.Include(x => x.Notifications).SingleOrDefaultAsync(x => x.Id == id && x.PhotographerId == photographerId);
        if (booking is null) return NotFound();
        var allowed = booking.Status == "Pending" && request.Status is ("Confirmed" or "Rejected")
            || booking.Status == "Accepted" && request.Status == "Confirmed";
        if (!allowed) return Conflict(new { message = "This status change is not allowed for the current booking state." });
        booking.Status = request.Status;
        BookingNotificationPlanner.Queue(booking, request.Status);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (exception.GetBaseException() is SqlException sql && sql.Number is 2601 or 2627)
        {
            return Conflict(new { message = "This booking status was already updated. Refresh the inquiries list." });
        }
        return NoContent();
    }

    [Authorize, HttpPatch("admin/bookings/{id}/follow-up")]
    public async Task<IActionResult> UpdateFollowUp(int id, UpdateBookingFollowUpRequest request)
    {
        var photographerId = await GetPhotographerId();
        if (photographerId is null) return Forbid();
        var booking = await db.Bookings.SingleOrDefaultAsync(x => x.Id == id && x.PhotographerId == photographerId);
        if (booking is null) return NotFound();
        booking.InternalNotes = string.IsNullOrWhiteSpace(request.InternalNotes) ? null : request.InternalNotes.Trim();
        booking.FollowUpUtc = request.FollowUpUtc?.ToUniversalTime();
        await db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize, HttpPatch("admin/bookings/{id}/financials")]
    public async Task<IActionResult> UpdateFinancials(int id, UpdateBookingFinancialsRequest request)
    {
        if (request.AgreedAmount is < 0 or > 9999999999.99m || request.AmountReceived < 0 || request.AmountReceived > 9999999999.99m)
            return BadRequest(new { message = "Amounts must be between 0 and 9,999,999,999.99." });
        if (decimal.Round(request.AgreedAmount ?? 0, 2) != (request.AgreedAmount ?? 0) || decimal.Round(request.AmountReceived, 2) != request.AmountReceived)
            return BadRequest(new { message = "Amounts may have at most two decimal places." });
        if (request.AgreedAmount is null && request.AmountReceived != 0)
            return BadRequest(new { message = "Set the agreed amount before recording money received." });
        if (request.AmountReceived > request.AgreedAmount)
            return BadRequest(new { message = "Amount received cannot exceed the agreed amount." });

        var photographerId = await GetPhotographerId();
        if (photographerId is null) return Forbid();
        var booking = await db.Bookings.SingleOrDefaultAsync(x => x.Id == id && x.PhotographerId == photographerId);
        if (booking is null) return NotFound();
        booking.AgreedAmount = request.AgreedAmount;
        booking.AmountReceived = request.AmountReceived;
        booking.Currency = request.Currency;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize, HttpPost("admin/bookings/{id}/notifications/{notificationId:long}/retry")]
    public async Task<IActionResult> RetryNotification(int id, long notificationId)
    {
        var photographerId = await GetPhotographerId();
        if (photographerId is null) return Forbid();
        var notification = await db.NotificationOutbox.SingleOrDefaultAsync(x =>
            x.Id == notificationId && x.BookingId == id && x.Booking!.PhotographerId == photographerId);
        if (notification is null) return NotFound();
        if (notification.EventType == "Accepted")
            return Conflict(new { message = "Accepted notifications are no longer sent. Confirm the booking to send a confirmation." });
        if (notification.Status != "Failed")
            return Conflict(new { message = "Only failed messages can be retried." });
        notification.Status = "Queued";
        notification.Attempts = 0;
        notification.NextAttemptUtc = DateTime.UtcNow;
        notification.LastError = null;
        await db.SaveChangesAsync();
        return NoContent();
    }
    private async Task<int?> GetPhotographerId()
    {
        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(subject, out var userId) ? await db.Photographers.Where(x => x.UserId == userId).Select(x => (int?)x.Id).SingleOrDefaultAsync() : null;
    }
}
