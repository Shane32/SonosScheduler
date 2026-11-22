using AppGraphQL.InputModels;
using AppServices;
using AppServices.Scheduling;
using AppServices.Sonos;
using AutoMapper;

namespace AppGraphQL.MutationGraphs;

public sealed class ScheduleMutation(AppDbContext db, IMapper mapper, AuditService audit, TimeProvider time, SonosService sonos) : DIObjectGraphBase
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

    public async Task<string> TestAsync([Id] int id, CancellationToken ct)
    {
        var schedule = await db.Schedules.AsNoTracking().Include(x => x.Speakers).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new ClientError("Schedule not found.");
        audit.Add("Schedule", $"Manually triggered '{schedule.Name}'.", id);
        await db.SaveChangesAsync(ct);

        string result;
        try {
            result = await sonos.PlayScheduleAsync(schedule, time.GetUtcNow(), ct);
        } catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) {
            var message = ex is ClientError ? $"Manual test failed: {ex.Message}" : "Manual test failed.";
            audit.Add("Schedule", message[..Math.Min(2000, message.Length)], id, "Error");
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        var outcome = $"Manual test: {result}";
        audit.Add("Schedule", outcome[..Math.Min(2000, outcome.Length)], id);
        await db.SaveChangesAsync(CancellationToken.None);
        return result;
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
