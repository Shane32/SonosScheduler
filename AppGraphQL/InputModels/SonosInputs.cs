namespace AppGraphQL.InputModels;

public sealed class GroupInput
{
    public required string Name { get; set; }
    public required string Subnet { get; set; }
    public string? SeedAddress { get; set; }
}

public sealed class ScheduleInput
{
    [Id] public int GroupId { get; set; }
    public required string Name { get; set; }
    public int DaysOfWeek { get; set; }
    public required string LocalTime { get; set; }
    public required string TimeZoneId { get; set; }
    public bool Enabled { get; set; }
    public required string LibraryObjectId { get; set; }
    public required string LibraryTitle { get; set; }
    public required string SelectionMode { get; set; }
    public bool OnlyIfIdle { get; set; }
    public required List<ScheduleSpeakerInput> Speakers { get; set; }
    public required List<ScheduleExclusionInput> Exceptions { get; set; }
}

public sealed class ScheduleSpeakerInput
{
    [Id] public int SpeakerId { get; set; }
    public bool Enabled { get; set; }
    public int? Volume { get; set; }
}

public sealed class ScheduleExclusionInput
{
    public required string Date { get; set; }
    public string? Reason { get; set; }
}
