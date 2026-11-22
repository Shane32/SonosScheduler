namespace AppGraphQL.QueryGraphs;

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
