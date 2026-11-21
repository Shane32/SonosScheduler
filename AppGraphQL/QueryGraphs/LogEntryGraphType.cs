namespace AppGraphQL.QueryGraphs;

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
