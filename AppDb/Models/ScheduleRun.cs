namespace AppDb.Models;

[Index(nameof(ScheduleId), nameof(ScheduledFor), IsUnique = true)]
public class ScheduleRun
{
    public long Id { get; set; }
    public int ScheduleId { get; set; }
    public DateTimeOffset ScheduledFor { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public ScheduleRunStatus Status { get; set; } = ScheduleRunStatus.Running;
    [MaxLength(2000)] public string Message { get; set; } = "";
}
