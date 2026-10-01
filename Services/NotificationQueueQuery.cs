using Lensora.Api.Domain;

namespace Lensora.Api.Services;

public static class NotificationQueueQuery
{
    // Processing entries become eligible only after their claim lease expires.
    // Use the same predicate for selection and atomic claiming.
    public static IQueryable<NotificationOutbox> ReadyToProcess(this IQueryable<NotificationOutbox> query, DateTime now) =>
        query.Where(x => x.EventType != "Accepted"
            && (x.Status == NotificationStatus.Queued || x.Status == NotificationStatus.Retry
                || x.Status == NotificationStatus.Processing)
            && x.NextAttemptUtc <= now);
}
