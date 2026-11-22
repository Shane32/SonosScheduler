namespace AppDb.Models;

[PrimaryKey(nameof(ScheduleId), nameof(SpeakerId))]
public class ScheduleSpeaker
{
    public int ScheduleId { get; set; }
    public Schedule Schedule { get; set; } = null!;
    public int SpeakerId { get; set; }
    public Speaker Speaker { get; set; } = null!;
    public bool Enabled { get; set; } = true;
    public int? Volume { get; set; }
}
