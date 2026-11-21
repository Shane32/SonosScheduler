namespace AppGraphQL.QueryGraphs;

public sealed class NetworkGroupGraphType : EfObjectGraphType<AppDbContext, NetworkGroup>
{
    public NetworkGroupGraphType()
    {
        EfIdField(x => x.Id);
        EfField(x => x.Name);
        EfField(x => x.Subnet);
        EfField(x => x.SeedAddress);
        EfField(x => x.LastRefreshedAt);
        EfField(x => x.LastError);
        EfField("Speakers", x => x.Id).DelayLoadList(ctx => ctx.DbContext.Speakers.OrderBy(x => x.Name).ThenBy(x => x.Id), x => x.GroupId);
    }
}

public sealed class SpeakerGraphType : EfObjectGraphType<AppDbContext, Speaker>
{
    public SpeakerGraphType()
    {
        EfIdField(x => x.Id);
        EfIdField(x => x.GroupId);
        EfField(x => x.SonosId);
        EfField(x => x.Name);
        EfField(x => x.Address);
        EfField(x => x.CoordinatorId);
        EfField(x => x.IsOnline);
        EfField(x => x.State);
        EfField(x => x.Title);
        EfField(x => x.Artist);
        EfField(x => x.Album);
        EfField(x => x.Volume);
        EfField(x => x.Muted);
        EfField(x => x.LastSeenAt);
        EfField(x => x.StatusUpdatedAt);
    }
}

public sealed class ScheduleGraphType : EfObjectGraphType<AppDbContext, Schedule>
{
    public ScheduleGraphType()
    {
        EfIdField(x => x.Id);
        EfIdField(x => x.GroupId);
        EfField(x => x.Name);
        EfField(x => x.DaysOfWeek);
        EfField(x => x.LocalTime);
        EfField(x => x.TimeZoneId);
        EfField(x => x.Enabled);
        EfField(x => x.DisabledUntil);
        EfField(x => x.LibraryObjectId);
        EfField(x => x.LibraryTitle);
        EfField(x => x.SelectionMode);
        EfField(x => x.OnlyIfIdle);
        EfField(x => x.UpdatedAt);
        EfField("Speakers", x => x.Id).DelayLoadList(ctx => ctx.DbContext.ScheduleSpeakers.OrderBy(x => x.SpeakerId), x => x.ScheduleId);
        EfField("Exceptions", x => x.Id).DelayLoadList(ctx => ctx.DbContext.ScheduleExclusions.OrderBy(x => x.Date).ThenBy(x => x.Id), x => x.ScheduleId);
    }
}

public sealed class ScheduleSpeakerGraphType : EfObjectGraphType<AppDbContext, ScheduleSpeaker>
{
    public ScheduleSpeakerGraphType() { EfIdField(x => x.ScheduleId); EfIdField(x => x.SpeakerId); EfField(x => x.Enabled); EfField(x => x.Volume); }
}

public sealed class ScheduleExclusionGraphType : EfObjectGraphType<AppDbContext, ScheduleExclusion>
{
    public ScheduleExclusionGraphType() { EfIdField(x => x.Id); EfField(x => x.Date); EfField(x => x.Reason); }
}

public sealed class AdministratorGrantGraphType : EfObjectGraphType<AppDbContext, AdministratorGrant>
{
    public AdministratorGrantGraphType() { EfIdField(x => x.Id); EfField(x => x.TenantId); EfField(x => x.ObjectId); EfField(x => x.DisplayName); }
}

public sealed class LogEntryGraphType : EfObjectGraphType<AppDbContext, LogEntry>
{
    public LogEntryGraphType()
    {
        EfIdField(x => x.Id);
        EfField(x => x.Timestamp);
        EfField(x => x.Level);
        EfField(x => x.Category);
        EfField(x => x.Message);
        EfField(x => x.Actor);
        EfField(x => x.Details);
        EfIdField(x => x.ScheduleId);
    }
}
