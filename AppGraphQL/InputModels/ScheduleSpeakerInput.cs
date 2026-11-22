namespace AppGraphQL.InputModels;

public sealed class ScheduleSpeakerInput
{
    [Id] public int SpeakerId { get; set; }
    public bool Enabled { get; set; }
    public int? Volume { get; set; }
}
