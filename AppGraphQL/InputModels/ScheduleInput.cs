namespace AppGraphQL.InputModels;

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
