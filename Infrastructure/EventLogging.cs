using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;

namespace Lensora.Api.Infrastructure;

public sealed class EventLogQueue
{
    public Channel<ApiEventLog> Channel { get; } = System.Threading.Channels.Channel.CreateBounded<ApiEventLog>(
        new BoundedChannelOptions(5000) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    public static string? Limit(string? value, int length) => value is null ? null : value[..Math.Min(value.Length, length)];
    public static void Fallback(ApiEventLog entry) => Console.Error.WriteLine(JsonSerializer.Serialize(entry));
    public void Write(ApiEventLog entry)
    {
        if (!Channel.Writer.TryWrite(entry)) Fallback(entry);
    }
}

// Persists only Lensora templates and explicitly allowed structured properties. Raw exception
// messages, request bodies, query strings, headers and scope strings never enter this store.
public sealed class EventLogProvider(EventLogQueue queue, IHttpContextAccessor accessor) : ILoggerProvider, ISupportExternalScope
{
    private readonly EventLogQueue queue = queue;
    private readonly IHttpContextAccessor accessor = accessor;
    private IExternalScopeProvider scopes = new LoggerExternalScopeProvider();
    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => scopes = scopeProvider;
    public ILogger CreateLogger(string categoryName) => new EventLogger(this, categoryName);
    public void Dispose() { }
    private sealed class EventLogger(EventLogProvider owner, string category) : ILogger
    {
        private static readonly HashSet<string> Allowed = ["NotificationId", "BookingId", "Channel", "EventType", "Attempt", "NotificationStatus", "ErrorType", "NextRetryUtc", "ItemId", "DisplayOrder", "Operation", "PhotographerId"];
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information && level < LogLevel.None && category.StartsWith("Lensora.Api", StringComparison.Ordinal);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner.scopes.Push(state);
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            var properties = new Dictionary<string, string?>();
            string? template = null;
            void Read(object? value, bool message)
            {
                if (value is not IEnumerable<KeyValuePair<string, object?>> pairs) return;
                foreach (var pair in pairs)
                {
                    if (message && pair.Key == "{OriginalFormat}") template = EventLogQueue.Limit(pair.Value?.ToString(), 2048);
                    else if (Allowed.Contains(pair.Key)) properties[pair.Key] = EventLogQueue.Limit(pair.Value?.ToString(), 256);
                }
            }
            owner.scopes.ForEachScope((scope, _) => Read(scope, false), 0);
            Read(state, true);
            if (exception is not null)
            {
                EventExceptionDetails.Add(properties, exception);
            }
            var details = JsonSerializer.Serialize(properties);
            if (details.Length > 8192)
            {
                properties.Remove("Stack");
                details = JsonSerializer.Serialize(properties);
                if (details.Length > 8192) details = "{\"Notice\":\"Event properties exceeded storage limit\"}";
            }
            var context = owner.accessor.HttpContext;
            owner.queue.Write(new ApiEventLog
            {
                Level = level.ToString(), Category = EventLogQueue.Limit(category, 256)!,
                TraceId = EventLogQueue.Limit(context?.TraceIdentifier, 128),
                UserId = EventLogQueue.Limit(context?.User.FindFirstValue(ClaimTypes.NameIdentifier), 128),
                Method = EventLogQueue.Limit(context?.Request.Method, 16),
                Route = EventLogQueue.Limit((context?.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText, 512),
                Message = template ?? "Application event (unstructured message omitted)",
                Details = properties.Count == 0 ? null : details
            });
        }
    }
}

public sealed class EventLogWriter(EventLogQueue queue, IServiceScopeFactory scopeFactory, IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextCleanup = DateTime.MinValue;
        try
        {
            while (await queue.Channel.Reader.WaitToReadAsync(stoppingToken))
            {
                var batch = new List<ApiEventLog>();
                while (batch.Count < 100 && queue.Channel.Reader.TryRead(out var entry)) batch.Add(entry);
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<EventLogDbContext>();
                    db.EventLogs.AddRange(batch);
                    await db.SaveChangesAsync(stoppingToken);
                }
                catch (Exception)
                {
                    Console.Error.WriteLine("Event log persistence unavailable; writing sanitized events to stderr.");
                    foreach (var entry in batch) EventLogQueue.Fallback(entry);
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }
                if (DateTime.UtcNow < nextCleanup) continue;
                nextCleanup = DateTime.UtcNow.AddHours(1);
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<EventLogDbContext>();
                    var days = Math.Clamp(configuration.GetValue("EventLogs:RetentionDays", 30), 1, 365);
                    var cutoff = DateTime.UtcNow.AddDays(-days);
                    await db.EventLogs.Where(x => x.TimestampUtc < cutoff).ExecuteDeleteAsync(stoppingToken);
                }
                catch (Exception) { Console.Error.WriteLine("Event log retention cleanup failed; will retry next hour."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            while (queue.Channel.Reader.TryRead(out var pending)) EventLogQueue.Fallback(pending);
        }
    }
}

public sealed class ApiEventLoggingMiddleware(RequestDelegate next, IConfiguration? configuration = null)
{
    public async Task InvokeAsync(HttpContext context, EventLogQueue queue)
    {
        var started = Stopwatch.GetTimestamp();
        var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
        var failed = false;
        var requestBody = await RequestBodyCapture.ReadAsync(context, configuration?.GetValue("EventLogs:CaptureRequestBodies", true) ?? true);
        context.Response.OnStarting(() => { context.Response.Headers["X-Request-ID"] = context.TraceIdentifier; return Task.CompletedTask; });
        try { await next(context); }
        catch { failed = true; throw; }
        finally
        {
            var status = context.RequestAborted.IsCancellationRequested ? 499 : failed ? 500 : context.Response.StatusCode;
            queue.Write(new ApiEventLog
            {
                Level = status >= 500 ? "Error" : status >= 400 ? "Warning" : "Information",
                Category = "Lensora.Api.Request",
                TraceId = EventLogQueue.Limit(context.TraceIdentifier, 128),
                UserId = EventLogQueue.Limit(context.User.FindFirstValue(ClaimTypes.NameIdentifier), 128),
                Method = EventLogQueue.Limit(context.Request.Method, 16),
                Route = EventLogQueue.Limit(route ?? "(unmatched route)", 512),
                StatusCode = status, DurationMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                Message = "HTTP request completed",
                Details = status >= 400 || (configuration?.GetValue("EventLogs:CaptureSuccessfulRequestBodies", false) ?? false) ? requestBody : null
            });
        }
    }
}
