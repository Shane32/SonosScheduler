using AppServices;

namespace AppGraphQL.MutationGraphs;

[Authorize(Policy = SecurityPolicies.ADMIN_POLICY)]
public sealed class AdminMutation(AdminService admins, AppDbContext db) : DIObjectGraphBase
{
    public async Task<EfSource<AdminUser>> AddAsync(string name, string password, CancellationToken ct)
    {
        var admin = await admins.AddAsync(name, password, ct);
        return await db.Admins.ToGraphSingleAsync(x => x.Id == admin.Id);
    }
    public async Task<bool> DeleteAsync([Id] int id, CancellationToken ct) { await admins.DeleteAsync(id, ct); return true; }
}
