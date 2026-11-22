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
