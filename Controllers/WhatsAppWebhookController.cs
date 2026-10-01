using Lensora.Api.Domain;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lensora.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lensora.Api.Controllers;

[ApiController]
[Route("api/webhooks/whatsapp")]
public sealed class WhatsAppWebhookController(IConfiguration configuration, LensoraDbContext db) : ControllerBase
{
    [HttpGet]
    public IActionResult Verify()
    {
        var token = configuration["WhatsApp:WebhookVerifyToken"];
        if (string.IsNullOrWhiteSpace(token)) return StatusCode(StatusCodes.Status503ServiceUnavailable);
        if (Request.Query["hub.mode"] != "subscribe" || Request.Query["hub.verify_token"] != token)
            return Forbid();
        return Content(Request.Query["hub.challenge"].ToString(), "text/plain");
    }

    [HttpPost]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        var secret = configuration["WhatsApp:AppSecret"];
        if (string.IsNullOrWhiteSpace(secret)) return StatusCode(StatusCodes.Status503ServiceUnavailable);
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, cancellationToken);
        var signature = Request.Headers["X-Hub-Signature-256"].ToString();
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), buffer.ToArray());
        byte[] received;
        try
        {
            received = Convert.FromHexString(signature.StartsWith("sha256=", StringComparison.Ordinal)
                ? signature[7..] : "");
        }
        catch (FormatException)
        {
            return Unauthorized();
        }
        if (received.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(received, expected))
            return Unauthorized();

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(buffer.ToArray());
        }
        catch (JsonException)
        {
            return BadRequest();
        }
        using (document)
        {
        if (!document.RootElement.TryGetProperty("entry", out var entries)) return Ok();
        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes)) continue;
            foreach (var change in changes.EnumerateArray())
            {
                if (!change.TryGetProperty("value", out var value)
                    || !value.TryGetProperty("statuses", out var statuses)) continue;
                foreach (var item in statuses.EnumerateArray())
                {
                    if (!item.TryGetProperty("id", out var idElement)
                        || !item.TryGetProperty("status", out var statusElement)) continue;
                    var messageId = idElement.GetString();
                    var status = statusElement.GetString();
                    if (messageId is null || status is null) continue;
                    var notification = await db.NotificationOutbox.SingleOrDefaultAsync(
                        x => x.Channel == "WhatsApp" && x.ProviderMessageId == messageId, cancellationToken);
                    if (notification is null || notification.Status == NotificationStatus.Cancelled) continue;
                    if (status == "read") notification.Status = NotificationStatus.Read;
                    else if (status == "delivered" && notification.Status != NotificationStatus.Read) notification.Status = NotificationStatus.Delivered;
                    else if (status == "failed" && notification.Status is not (NotificationStatus.Delivered or NotificationStatus.Read))
                    {
                        notification.Status = NotificationStatus.Failed;
                        notification.LastError = "WhatsApp reported a delivery failure.";
                    }
                }
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return Ok();
        }
    }
}
