namespace AppDb.Models;

[Index(nameof(Timestamp))]
public class LogEntry
{
    public long Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    [MaxLength(20)] public string Level { get; set; } = "Information";
    [MaxLength(50)] public string Category { get; set; } = "";
    [MaxLength(2000)] public string Message { get; set; } = "";
    [MaxLength(200)] public string Actor { get; set; } = "System";
    public string? Details { get; set; }
    public int? ScheduleId { get; set; }
}
