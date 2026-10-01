using Lensora.Api.Domain;
using Lensora.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lensora.Api.Services;

public sealed class NotificationWorker(IServiceScopeFactory scopeFactory, ILogger<NotificationWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Notification worker started.");
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
            .ReadyToProcess(now)
            .OrderBy(x => x.NextAttemptUtc).Select(x => x.Id).Take(10).ToListAsync(cancellationToken);

        foreach (var id in ids)
        {
            var claimed = await db.NotificationOutbox
                .ReadyToProcess(now).Where(x => x.Id == id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, NotificationStatus.Processing)
                    .SetProperty(x => x.Attempts, x => x.Attempts + 1)
                    .SetProperty(x => x.NextAttemptUtc, now.AddMinutes(5)), cancellationToken);
            if (claimed == 0) continue;

            var notification = await db.NotificationOutbox.Include(x => x.Booking)!
                .ThenInclude(x => x!.Package).SingleAsync(x => x.Id == id, cancellationToken);
            var booking = notification.Booking!;
            using var logScope = logger.BeginScope(new Dictionary<string, object>
            {
                ["NotificationId"] = notification.Id,
                ["BookingId"] = notification.BookingId,
                ["Channel"] = notification.Channel,
                ["EventType"] = notification.EventType,
                ["Attempt"] = notification.Attempts
            });
            logger.LogInformation("Notification attempt started.");
            var photographerName = await db.Photographers.Where(x => x.Id == booking.PhotographerId)
                .Select(x => x.Name).SingleAsync(cancellationToken);
            try
            {
                notification.ProviderMessageId = await sender.SendAsync(notification, booking,
                    photographerName, booking.Package?.Name, cancellationToken);
                notification.Status = NotificationStatus.Sent;
                notification.SentUtc = DateTime.UtcNow;
                notification.LastError = null;
                logger.LogInformation("Notification accepted by provider. Delivery to the recipient is not yet verified.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                notification.Status = exception is NotificationConfigurationException || notification.Attempts >= 5
                    ? NotificationStatus.Failed : NotificationStatus.Retry;
                notification.NextAttemptUtc = DateTime.UtcNow.AddMinutes(Math.Min(60, Math.Pow(2, notification.Attempts)));
                var safeError = exception is NotificationConfigurationException or NotificationDeliveryException
                    ? exception.Message : $"Notification send failed ({exception.GetType().Name}). Check provider connectivity and configuration.";
                notification.LastError = safeError;
                logger.Log(notification.Status == NotificationStatus.Failed ? LogLevel.Error : LogLevel.Warning, exception,
                    "Notification attempt failed. Status: {NotificationStatus}; Error: {Error}; ErrorType: {ErrorType}; Next retry: {NextRetryUtc}",
                    notification.Status, safeError, exception.GetType().Name,
                    notification.Status == NotificationStatus.Retry ? notification.NextAttemptUtc : (DateTime?)null);
            }
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Notification result persisted. Status: {NotificationStatus}", notification.Status);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Could not persist notification result. The provider may already have accepted the message; check before retrying.");
                throw;
            }
            db.ChangeTracker.Clear();
        }
    }
}
