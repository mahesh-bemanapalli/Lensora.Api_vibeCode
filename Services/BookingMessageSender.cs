using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using Lensora.Api.Domain;

namespace Lensora.Api.Services;

public sealed class NotificationConfigurationException(string message) : Exception(message);
public sealed class NotificationDeliveryException(string message) : Exception(message);

public sealed class BookingMessageSender(IConfiguration configuration, IHttpClientFactory httpClientFactory)
{
    public async Task<string?> SendAsync(NotificationOutbox notification, Booking booking,
        string photographerName, string? packageName, CancellationToken cancellationToken)
    {
        var date = booking.EventDate.ToString("dd MMM yyyy");
        var subject = notification.EventType switch
        {
            "InquiryReceived" => "We received your photography inquiry",
            "NewInquiry" => "New booking inquiry",
            "Rejected" => "Update on your photography inquiry",
            "Confirmed" => "Your photography booking is confirmed",
            _ => throw new InvalidOperationException("Unknown notification event.")
        };
        var summary = $"Event date: {date}\nPackage: {packageName ?? "General inquiry"}\nReference: {booking.Id}";
        var body = notification.EventType switch
        {
            "InquiryReceived" => $"Hello {booking.ClientName},\n\nWe received your inquiry for {photographerName}. This is not yet a confirmed booking. We will follow up soon.\n\n{summary}",
            "NewInquiry" => $"A new inquiry arrived from {booking.ClientName} ({booking.ClientEmail}).\n\n{summary}\n\nOpen the Lensora dashboard to respond.",
            "Rejected" => $"Hello {booking.ClientName},\n\n{photographerName} cannot accept your inquiry for this date. You may contact the photographer about alternatives.\n\n{summary}",
            "Confirmed" => $"Hello {booking.ClientName},\n\nYour session with {photographerName} has been confirmed.\n\n{summary}",
            _ => throw new InvalidOperationException("Unknown notification event.")
        };

        if (notification.Channel == "Email")
        {
            await SendEmailAsync(notification.Recipient, subject, body, cancellationToken);
            return null;
        }
        if (notification.Channel == "WhatsApp")
            return await SendWhatsAppAsync(notification, booking.ClientName, date, cancellationToken);
        throw new InvalidOperationException("Unknown notification channel.");
    }

    private async Task SendEmailAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
    {
        var host = configuration["Email:SmtpHost"];
        var from = configuration["Email:FromAddress"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
            throw new NotificationConfigurationException("Configure Email:SmtpHost and Email:FromAddress.");
        var port = configuration.GetValue("Email:SmtpPort", 587);
        using var client = new SmtpClient(host, port)
        {
            EnableSsl = configuration.GetValue("Email:EnableSsl", true),
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        var user = configuration["Email:Username"];
        if (!string.IsNullOrWhiteSpace(user))
            client.Credentials = new NetworkCredential(user, configuration["Email:Password"]);
        using var message = new MailMessage(from, recipient, subject, body);
        try
        {
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (SmtpException exception)
        {
            // Provider exception text can contain recipient addresses; retain the code only.
            throw new NotificationDeliveryException($"SMTP send failed. Status: {exception.StatusCode} ({(int)exception.StatusCode}).");
        }
    }

    private async Task<string?> SendWhatsAppAsync(NotificationOutbox notification, string clientName,
        string eventDate, CancellationToken cancellationToken)
    {
        var version = configuration["WhatsApp:GraphApiVersion"];
        var phoneNumberId = configuration["WhatsApp:PhoneNumberId"];
        var accessToken = configuration["WhatsApp:AccessToken"];
        var templateName = configuration[$"WhatsApp:Templates:{notification.EventType}"];
        if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(phoneNumberId)
            || string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(templateName))
            throw new NotificationConfigurationException($"Configure WhatsApp credentials and the {notification.EventType} template.");

        var payload = new
        {
            messaging_product = "whatsapp",
            to = notification.Recipient.TrimStart('+'),
            type = "template",
            template = new
            {
                name = templateName,
                language = new { code = configuration["WhatsApp:LanguageCode"] ?? "en_US" },
                components = new[]
                {
                    new { type = "body", parameters = new[]
                    {
                        new { type = "text", text = clientName },
                        new { type = "text", text = eventDate }
                    }}
                }
            }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://graph.facebook.com/{version}/{phoneNumberId}/messages")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var details = "";
            try
            {
                await using var errorStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var errorDocument = await JsonDocument.ParseAsync(errorStream, cancellationToken: cancellationToken);
                if (errorDocument.RootElement.ValueKind == JsonValueKind.Object && errorDocument.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                {
                    if (error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var number)) details += $" Code: {number}.";
                    if (error.TryGetProperty("error_subcode", out var subcode) && subcode.ValueKind == JsonValueKind.Number && subcode.TryGetInt32(out var subnumber)) details += $" Subcode: {subnumber}.";
                }
            }
            catch (JsonException) { /* Non-JSON provider errors still retain the HTTP status. */ }
            throw new NotificationDeliveryException($"WhatsApp API returned HTTP {(int)response.StatusCode}.{details}");
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.TryGetProperty("messages", out var messages) && messages.GetArrayLength() > 0
            ? messages[0].GetProperty("id").GetString()
            : null;
    }
}
