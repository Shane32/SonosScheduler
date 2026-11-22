using System.Net;
using AppGraphQL.InputModels;
using AppServices;
using AppServices.Sonos;
using AutoMapper;

namespace AppGraphQL.MutationGraphs;

public sealed class GroupMutation(AppDbContext db, IMapper mapper, SonosService sonos, AuditService audit) : DIObjectGraphBase
{
    [Authorize(Policy = SecurityPolicies.ADMIN_POLICY)]
    public async Task<EfSource<NetworkGroup>> AddAsync(GroupInput input, CancellationToken ct)
    {
        Validate(input);
        var group = mapper.Map<NetworkGroup>(input);
        db.Groups.Add(group);
        audit.Add("Network", $"Added subnet group '{group.Name}' ({group.Subnet}).");
        await db.SaveChangesAsync(ct);
        return await db.Groups.ToGraphSingleAsync(x => x.Id == group.Id);
    }

    [Authorize(Policy = SecurityPolicies.ADMIN_POLICY)]
    public async Task<EfSource<NetworkGroup>> UpdateAsync([Id] int id, GroupInput input, CancellationToken ct)
    {
        Validate(input);
        var group = await db.Groups.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ClientError("Subnet group not found.");
        if (group.Subnet != input.Subnet && await db.Speakers.AnyAsync(x => x.GroupId == id, ct))
            throw new ClientError("Create a new subnet group to move to another network; existing rooms are referenced by schedules.");
        mapper.Map(input, group);
        audit.Add("Network", $"Updated subnet group '{group.Name}'.");
        await db.SaveChangesAsync(ct);
        return await db.Groups.ToGraphSingleAsync(x => x.Id == id);
    }

    [Authorize(Policy = SecurityPolicies.ADMIN_POLICY)]
    public async Task<bool> DeleteAsync([Id] int id, CancellationToken ct)
    {
        if (await db.Schedules.AnyAsync(x => x.GroupId == id, ct))
            throw new ClientError("Delete this group's schedules first.");
        var group = await db.Groups.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ClientError("Subnet group not found.");
        db.Groups.Remove(group);
        audit.Add("Network", $"Deleted subnet group '{group.Name}'.");
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RefreshAsync([Id] int id, CancellationToken ct)
    {
        await sonos.RefreshAsync(id, ct);
        return true;
    }

    private static void Validate(GroupInput input)
    {
        input.Name = input.Name.Trim();
        if (input.Name.Length is < 1 or > 100)
            throw new ClientError("Enter a group name of at most 100 characters.");
        IPv4Subnet subnet;
        try { subnet = IPv4Subnet.Parse(input.Subnet); } catch (InvalidDataException ex) { throw new ClientError(ex.Message); }
        input.Subnet = subnet.ToString();
        input.SeedAddress = string.IsNullOrWhiteSpace(input.SeedAddress) ? null : input.SeedAddress.Trim();
        if (input.SeedAddress is not null && (!IPAddress.TryParse(input.SeedAddress, out var address) || !subnet.Contains(address)))
            throw new ClientError("Known speaker address must be an IPv4 address inside the subnet.");
    }
}
