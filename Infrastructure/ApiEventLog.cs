using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Lensora.Api.Infrastructure;

public sealed class ApiEventLog
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(16)] public string Level { get; set; } = "Information";
    [MaxLength(256)] public string Category { get; set; } = "";
    [MaxLength(128)] public string? TraceId { get; set; }
    [MaxLength(128)] public string? UserId { get; set; }
    [MaxLength(16)] public string? Method { get; set; }
    [MaxLength(512)] public string? Route { get; set; }
    public int? StatusCode { get; set; }
    public long? DurationMs { get; set; }
    [MaxLength(2048)] public string Message { get; set; } = "";
    [MaxLength(8192)] public string? Details { get; set; }
}

public sealed class EventLogDbContext(DbContextOptions<EventLogDbContext> options) : DbContext(options)
{
    public DbSet<ApiEventLog> EventLogs => Set<ApiEventLog>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<ApiEventLog>().ToTable("ApiEventLogs");
        model.Entity<ApiEventLog>().HasIndex(x => x.TimestampUtc);
        model.Entity<ApiEventLog>().HasIndex(x => new { x.TraceId, x.Id });
    }
}
