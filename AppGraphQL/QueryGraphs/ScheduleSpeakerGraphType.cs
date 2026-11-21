namespace AppGraphQL.QueryGraphs;

public sealed class ScheduleSpeakerGraphType : EfObjectGraphType<AppDbContext, ScheduleSpeaker>
{
    public ScheduleSpeakerGraphType() { EfIdField(x => x.ScheduleId); EfIdField(x => x.SpeakerId); EfField(x => x.Enabled); EfField(x => x.Volume); }
}
