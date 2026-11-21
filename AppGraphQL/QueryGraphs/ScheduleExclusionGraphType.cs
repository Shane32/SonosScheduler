namespace AppGraphQL.QueryGraphs;

public sealed class ScheduleExclusionGraphType : EfObjectGraphType<AppDbContext, ScheduleExclusion>
{
    public ScheduleExclusionGraphType()
    {
        EfIdField(x => x.Id);
        EfField(x => x.Date);
        EfField(x => x.Reason);
    }
}
