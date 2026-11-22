namespace AppDb.Models;

public class ScheduleExclusion
{
    public int Id { get; set; }
    public int ScheduleId { get; set; }
    public Schedule Schedule { get; set; } = null!;
    [MaxLength(10)] public string Date { get; set; } = "";
    [MaxLength(500)] public string? Reason { get; set; }
}
