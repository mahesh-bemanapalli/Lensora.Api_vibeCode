using Lensora.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lensora.Api.Controllers;

[ApiController, Authorize, Route("api/admin/event-logs")]
public sealed class EventLogsController(EventLogDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc,
        [FromQuery] string? level, [FromQuery] string? route, [FromQuery] string? traceId,
        [FromQuery] int? statusCode, [FromQuery] long? beforeId, [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (pageSize is < 1 or > 100 || beforeId <= 0 || statusCode is < 100 or > 599 ||
            route?.Length > 512 || traceId?.Length > 128 ||
            (level is not null && level is not ("Information" or "Warning" or "Error" or "Critical")))
            return BadRequest(new { title = "Invalid log filter." });
        var start = fromUtc?.ToUniversalTime() ?? DateTime.UtcNow.AddDays(-1);
        var end = toUtc?.ToUniversalTime() ?? DateTime.UtcNow.AddMinutes(1);
        if (start > end) return BadRequest(new { title = "Start time must precede end time." });
        var query = db.EventLogs.AsNoTracking().Where(x => x.TimestampUtc >= start && x.TimestampUtc <= end);
        if (!string.IsNullOrWhiteSpace(level)) query = query.Where(x => x.Level == level);
        if (!string.IsNullOrWhiteSpace(route)) query = query.Where(x => x.Route != null && x.Route.Contains(route));
        if (!string.IsNullOrWhiteSpace(traceId)) query = query.Where(x => x.TraceId == traceId);
        if (statusCode.HasValue) query = query.Where(x => x.StatusCode == statusCode);
        if (beforeId.HasValue) query = query.Where(x => x.Id < beforeId);
        var items = await query.OrderByDescending(x => x.Id).Take(pageSize + 1).ToListAsync(cancellationToken);
        var hasMore = items.Count > pageSize;
        if (hasMore) items.RemoveAt(pageSize);
        return Ok(new { items, nextBeforeId = hasMore ? items[^1].Id : (long?)null });
    }
}
