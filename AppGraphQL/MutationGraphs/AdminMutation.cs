using AppServices;

namespace AppGraphQL.MutationGraphs;

[Authorize(Policy = SecurityPolicies.ADMIN_POLICY)]
public sealed class AdminMutation(AdminService admins, AppDbContext db) : DIObjectGraphBase
{
    public async Task<EfSource<AdministratorGrant>> AddAsync(Guid objectId, string? displayName, CancellationToken ct)
    {
        var admin = await admins.AddAsync(objectId, ct, displayName);
        return await db.Administrators.ToGraphSingleAsync(x => x.Id == admin.Id);
    }
    public async Task<bool> DeleteAsync([Id] int id, CancellationToken ct)
    {
        await admins.DeleteAsync(id, ct);
        return true;
    }
}
