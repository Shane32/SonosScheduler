using AppGraphQL.InputModels;
using AppServices;
using AppServices.Scheduling;
using AutoMapper;

namespace AppGraphQL.MutationGraphs;

public sealed class ScheduleMutation(AppDbContext db, IMapper mapper, AuditService audit, TimeProvider time) : DIObjectGraphBase
{
    public async Task<EfSource<Schedule>> AddAsync(ScheduleInput input, CancellationToken ct)
    {
        var schedule = mapper.Map<Schedule>(input);
        MapChildren(input, schedule);
        await ValidateAsync(schedule, ct);
        db.Schedules.Add(schedule);
        audit.Add("ScheduleEdit", $"Created schedule '{schedule.Name}'.");
        await db.SaveChangesAsync(ct);
        return await db.Schedules.ToGraphSingleAsync(x => x.Id == schedule.Id);
    }

    public async Task<EfSource<Schedule>> UpdateAsync([Id] int id, ScheduleInput input, CancellationToken ct)
    {
        var schedule = await db.Schedules.Include(x => x.Speakers).Include(x => x.Exceptions).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ClientError("Schedule not found.");
        mapper.Map(input, schedule);
        var oldRooms = schedule.Speakers.ToDictionary(x => x.SpeakerId);
        foreach (var old in schedule.Speakers.Where(x => !input.Speakers.Any(i => i.SpeakerId == x.SpeakerId)).ToArray())
            schedule.Speakers.Remove(old);
        foreach (var setting in input.Speakers) {
            if (oldRooms.TryGetValue(setting.SpeakerId, out var old))
                mapper.Map(setting, old);
            else
                schedule.Speakers.Add(mapper.Map<ScheduleSpeaker>(setting));
        }
        schedule.Exceptions.Clear();
        schedule.Exceptions.AddRange(input.Exceptions.Select(mapper.Map<ScheduleExclusion>));
        await ValidateAsync(schedule, ct);
        audit.Add("ScheduleEdit", $"Updated schedule '{schedule.Name}'.", id, details: System.Text.Json.JsonSerializer.Serialize(input));
        await db.SaveChangesAsync(ct);
        return await db.Schedules.ToGraphSingleAsync(x => x.Id == id);
    }

    public async Task<bool> DeleteAsync([Id] int id, CancellationToken ct)
    {
        var schedule = await FindAsync(id, ct);
        db.Schedules.Remove(schedule);
        audit.Add("ScheduleEdit", $"Deleted schedule '{schedule.Name}'.", id);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetEnabledAsync([Id] int id, bool enabled, CancellationToken ct)
    {
        var schedule = await FindAsync(id, ct);
        schedule.Enabled = enabled;
        schedule.DisabledUntil = null;
        schedule.UpdatedAt = time.GetUtcNow();
        audit.Add("ScheduleEdit", $"{(enabled ? "Enabled" : "Disabled")} schedule '{schedule.Name}'.", id);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DisableForAsync([Id] int id, int days, CancellationToken ct)
    {
        if (days is < 1 or > 365)
            throw new ClientError("Pause duration must be 1–365 days.");
        var schedule = await FindAsync(id, ct);
        schedule.DisabledUntil = time.GetUtcNow().AddDays(days);
        schedule.UpdatedAt = time.GetUtcNow();
        audit.Add("ScheduleEdit", $"Paused schedule '{schedule.Name}' until {schedule.DisabledUntil:O}.", id);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<Schedule> FindAsync(int id, CancellationToken ct) =>
        await db.Schedules.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ClientError("Schedule not found.");
    private void MapChildren(ScheduleInput input, Schedule schedule)
    {
        schedule.Speakers = input.Speakers.Select(mapper.Map<ScheduleSpeaker>).ToList();
        schedule.Exceptions = input.Exceptions.Select(mapper.Map<ScheduleExclusion>).ToList();
    }
    private async Task ValidateAsync(Schedule schedule, CancellationToken ct)
    {
        ScheduleClock.Validate(schedule);
        if (!await db.Groups.AnyAsync(x => x.Id == schedule.GroupId, ct))
            throw new ClientError("Subnet group not found.");
        var ids = schedule.Speakers.Select(x => x.SpeakerId).ToArray();
        if (await db.Speakers.CountAsync(x => ids.Contains(x.Id) && x.GroupId == schedule.GroupId, ct) != ids.Length)
            throw new ClientError("Every selected speaker must belong to this subnet group.");
    }
}
