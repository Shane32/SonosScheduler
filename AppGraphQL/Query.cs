using AppGraphQL.OutputModels;
using AppServices.Sonos;

#pragma warning disable IDE0060 // Cancellation tokens are consumed by the GraphQL.Linq request context.

namespace AppGraphQL;

public sealed class Query(AppDbContext db, SonosService sonos) : DIObjectGraphBase
{
    public SessionInfo Session() => new(User?.IsInRole("Admin") == true, User?.Identity?.Name);
    public Task<Connection<EfSource<NetworkGroup>>> GroupsAsync(int? first, int? last, [Id] string? before, [Id] string? after, CancellationToken ct)
        => db.Groups.OrderBy(x => x.Name).ThenBy(x => x.Id).ToGraphConnectionAsync(first, last, after, before, 100);
    public Task<Connection<EfSource<Schedule>>> SchedulesAsync([Id] int? groupId, int? first, int? last, [Id] string? before, [Id] string? after, CancellationToken ct)
        => db.Schedules.Where(x => groupId == null || x.GroupId == groupId).OrderBy(x => x.Name).ThenBy(x => x.Id).ToGraphConnectionAsync(first, last, after, before, 100);
    [Authorize(Policy = SecurityPolicies.ADMIN_POLICY)]
    public Task<Connection<EfSource<AdministratorGrant>>> AdminsAsync(int? first, int? last, [Id] string? before, [Id] string? after, CancellationToken ct)
        => db.Administrators.OrderBy(x => x.Id).ToGraphConnectionAsync(first, last, after, before, 100);
    [Authorize(Policy = SecurityPolicies.ADMIN_POLICY)]
    public Task<Connection<EfSource<LogEntry>>> LogsAsync(int? first, int? last, [Id] string? before, [Id] string? after, CancellationToken ct)
        => db.Logs.OrderByDescending(x => x.Id).ToGraphConnectionAsync(first, last, after, before, 100);
    public async Task<LibraryPageInfo> LibraryAsync([Id] int groupId, string objectId, int start, int count, CancellationToken ct)
        => LibraryPageInfo.From(await sonos.BrowseAsync(groupId, objectId, start, count, ct));
}
