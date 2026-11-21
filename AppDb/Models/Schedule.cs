namespace AppDb.Models;

public class Schedule
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public NetworkGroup Group { get; set; } = null!;
    [MaxLength(100)] public string Name { get; set; } = "";
    public int DaysOfWeek { get; set; } = 127;
    [MaxLength(5)] public string LocalTime { get; set; } = "08:00";
    [MaxLength(100)] public string TimeZoneId { get; set; } = "America/New_York";
    public bool Enabled { get; set; } = true;
    public DateTimeOffset? DisabledUntil { get; set; }
    [MaxLength(2000)] public string LibraryObjectId { get; set; } = "";
    [MaxLength(500)] public string LibraryTitle { get; set; } = "";
    [MaxLength(30)] public string SelectionMode { get; set; } = "PlayAll";
    public bool OnlyIfIdle { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = null!;
    public List<ScheduleSpeaker> Speakers { get; set; } = [];
    public List<ScheduleExclusion> Exceptions { get; set; } = [];
}
