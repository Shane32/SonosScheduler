namespace AppGraphQL.QueryGraphs;

public sealed class AdministratorGrantGraphType : EfObjectGraphType<AppDbContext, AdministratorGrant>
{
    public AdministratorGrantGraphType()
    {
        EfIdField(x => x.Id);
        EfField(x => x.TenantId);
        EfField(x => x.ObjectId);
        EfField(x => x.DisplayName);
    }
}
