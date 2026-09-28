using Lensora.Api.Domain;

namespace Lensora.Api.Services;

public static class BookingNotificationPlanner
{
    public static void Queue(Booking booking, string eventType, string? photographerEmail = null)
    {
        var now = DateTime.UtcNow;
        booking.Notifications.Add(NewMessage(booking, eventType, "Email", booking.ClientEmail, now));

        if (booking.WhatsAppOptIn && !string.IsNullOrWhiteSpace(booking.ClientPhone))
            booking.Notifications.Add(NewMessage(booking, eventType, "WhatsApp", booking.ClientPhone, now));

        if (eventType == "InquiryReceived" && !string.IsNullOrWhiteSpace(photographerEmail))
            booking.Notifications.Add(NewMessage(booking, "NewInquiry", "Email", photographerEmail, now));
    }

    private static NotificationOutbox NewMessage(Booking booking, string eventType, string channel, string recipient, DateTime now) => new()
    {
        Booking = booking,
        EventType = eventType,
        Channel = channel,
        Recipient = recipient,
        Status = "Queued",
        CreatedUtc = now,
        NextAttemptUtc = now
    };
}
