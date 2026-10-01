using System.Text.Json;

namespace Lensora.Api.Infrastructure;

public static class RequestBodyCapture
{
    // Unknown fields are redacted, including nested values. Expand deliberately when new
    // request contracts need diagnostics. Never capture authentication or webhook payloads.
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "internalNotes", "followUpUtc", "status", "eventDate", "packageId", "message",
        "whatsAppOptIn", "agreedAmount", "amountReceived", "currency", "name", "description",
        "title", "category", "brand", "model", "isFeatured", "displayOrder", "isPublished",
        "coverageHours", "deliverables", "bio", "location", "slug"
    };

    public static async Task<string?> ReadAsync(HttpContext context, bool enabled)
    {
        if (!enabled || !(HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method)
            || HttpMethods.IsPatch(context.Request.Method) || HttpMethods.IsDelete(context.Request.Method))) return null;
        var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText?.TrimStart('/');
        if (route is null || !route.StartsWith("api/", StringComparison.OrdinalIgnoreCase)
            || route.StartsWith("api/auth", StringComparison.OrdinalIgnoreCase)
            || route.StartsWith("api/webhooks", StringComparison.OrdinalIgnoreCase)) return null;
        if (!context.Request.HasJsonContentType()) return JsonSerializer.Serialize(new { RequestBodyCapture = "Skipped: non-JSON body" });
        const int maxBytes = 16384;
        if (context.Request.ContentLength > maxBytes) return JsonSerializer.Serialize(new { RequestBodyCapture = "Skipped: body exceeds 16 KB" });
        try
        {
            context.Request.EnableBuffering();
            var buffer = new byte[maxBytes + 1];
            var count = 0;
            while (count < buffer.Length)
            {
                var read = await context.Request.Body.ReadAsync(buffer.AsMemory(count), context.RequestAborted);
                if (read == 0) break;
                count += read;
            }
            if (count == 0) return null;
            if (count > maxBytes) return JsonSerializer.Serialize(new { RequestBodyCapture = "Skipped: body exceeds 16 KB" });
            using var document = JsonDocument.Parse(buffer.AsMemory(0, count), new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return JsonSerializer.Serialize(new { RequestBodyCapture = "Skipped: JSON object required" });
            var fields = new Dictionary<string, object?>();
            var limited = false;
            foreach (var field in document.RootElement.EnumerateObject())
            {
                // Unknown property names may themselves contain sensitive data.
                if (!Allowed.Contains(field.Name)) { fields["_redactedFields"] = true; continue; }
                object? value = field.Value.ValueKind switch
                {
                    JsonValueKind.String => EventLogQueue.Limit(field.Value.GetString(), 2000),
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => field.Value.Clone(),
                    _ => "[Nested value omitted]"
                };
                if (field.Value.ValueKind == JsonValueKind.String && field.Value.GetString()!.Length > 2000) limited = true;
                fields[field.Name] = value;
                if (JsonSerializer.Serialize(fields).Length > 6500)
                {
                    fields.Remove(field.Name);
                    limited = true;
                }
            }
            return JsonSerializer.Serialize(new { RequestBodyCapture = limited ? "Partial: size limit applied" : "Captured: allowed fields only", RequestBody = fields });
        }
        catch (JsonException) { return JsonSerializer.Serialize(new { RequestBodyCapture = "Skipped: malformed or deeply nested JSON" }); }
        catch (IOException) { return JsonSerializer.Serialize(new { RequestBodyCapture = "Skipped: body could not be read" }); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { return null; }
        finally
        {
            if (context.Request.Body.CanSeek) context.Request.Body.Position = 0;
        }
    }
}
