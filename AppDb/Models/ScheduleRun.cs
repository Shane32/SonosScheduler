namespace AppDb.Models;

[Index(nameof(ScheduleId), nameof(ScheduledFor), IsUnique = true)]
public class ScheduleRun
{
    public long Id { get; set; }
    public int ScheduleId { get; set; }
    public DateTimeOffset ScheduledFor { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    [MaxLength(30)] public string Status { get; set; } = "Running";
    [MaxLength(2000)] public string Message { get; set; } = "";
}
