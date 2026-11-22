namespace AppGraphQL.InputModels;

public sealed class ScheduleExclusionInput
{
    public required string Date { get; set; }
    public string? Reason { get; set; }
}
