using Lensora.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lensora.Api.Services;

public sealed class NotificationWorker(IServiceScopeFactory scopeFactory, ILogger<NotificationWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatch(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Notification worker failed while checking queued messages.");
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ProcessBatch(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LensoraDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<BookingMessageSender>();
        var now = DateTime.UtcNow;
        var ids = await db.NotificationOutbox.AsNoTracking()
            .Where(x => (x.Status == "Queued" || x.Status == "Retry" || x.Status == "Processing")
                && x.NextAttemptUtc <= now)
            .OrderBy(x => x.NextAttemptUtc).Select(x => x.Id).Take(10).ToListAsync(cancellationToken);

        foreach (var id in ids)
        {
            var claimed = await db.NotificationOutbox
                .Where(x => x.Id == id && (x.Status == "Queued" || x.Status == "Retry" || x.Status == "Processing")
                    && x.NextAttemptUtc <= now)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, "Processing")
                    .SetProperty(x => x.Attempts, x => x.Attempts + 1)
                    .SetProperty(x => x.NextAttemptUtc, now.AddMinutes(5)), cancellationToken);
            if (claimed == 0) continue;

            var notification = await db.NotificationOutbox.Include(x => x.Booking)!
                .ThenInclude(x => x!.Package).SingleAsync(x => x.Id == id, cancellationToken);
            var booking = notification.Booking!;
            var photographerName = await db.Photographers.Where(x => x.Id == booking.PhotographerId)
                .Select(x => x.Name).SingleAsync(cancellationToken);
            try
            {
                notification.ProviderMessageId = await sender.SendAsync(notification, booking,
                    photographerName, booking.Package?.Name, cancellationToken);
                notification.Status = "Sent";
                notification.SentUtc = DateTime.UtcNow;
                notification.LastError = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                notification.Status = exception is NotificationConfigurationException || notification.Attempts >= 5
                    ? "Failed" : "Retry";
                notification.NextAttemptUtc = DateTime.UtcNow.AddMinutes(Math.Min(60, Math.Pow(2, notification.Attempts)));
                notification.LastError = exception.Message.Length > 1000 ? exception.Message[..1000] : exception.Message;
                logger.LogWarning(exception, "Notification {NotificationId} failed on attempt {Attempt}.", id, notification.Attempts);
            }
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }
    }
}
