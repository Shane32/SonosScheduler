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

public class ScheduleExclusion
{
    public int Id { get; set; }
    public int ScheduleId { get; set; }
    public Schedule Schedule { get; set; } = null!;
    [MaxLength(10)] public string Date { get; set; } = "";
    [MaxLength(500)] public string? Reason { get; set; }
}

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
