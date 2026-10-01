namespace Lensora.Api.Domain;

public static class NotificationStatus
{
    public const string Queued = "Queued";
    public const string Processing = "Processing";
    public const string Retry = "Retry";
    public const string Sent = "Sent";
    public const string Delivered = "Delivered";
    public const string Read = "Read";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}
