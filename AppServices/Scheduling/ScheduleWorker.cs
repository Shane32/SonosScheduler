using AppServices.Sonos;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;

namespace AppServices.Scheduling;

public sealed class ScheduleWorker(IServiceScopeFactory scopes, TimeProvider time, ExceptionLogService errors) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15), time);
        do {
            try { await TickAsync(stoppingToken); } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; } catch (Exception ex) { await errors.RecordAsync(ex, "Scheduler", cancellationToken: CancellationToken.None); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task TickAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var schedules = await db.Schedules.AsNoTracking().AsSplitQuery().Include(x => x.Speakers).Include(x => x.Exceptions).Where(x => x.Enabled).ToListAsync(ct);
        var tasks = new List<Task>();
        foreach (var schedule in schedules) {
            foreach (var occurrence in ScheduleClock.GetDueOccurrences(schedule, time.GetUtcNow(), TimeSpan.FromMinutes(5)))
                tasks.Add(RunAsync(schedule.Id, occurrence, ct));
        }
        await Task.WhenAll(tasks);
    }

    private async Task RunAsync(int id, DateTimeOffset occurrence, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.ScheduleRuns.AnyAsync(x => x.ScheduleId == id && x.ScheduledFor == occurrence, ct))
            return;
        var run = new ScheduleRun { ScheduleId = id, ScheduledFor = occurrence, StartedAt = time.GetUtcNow() };
        db.ScheduleRuns.Add(run);
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 }) { return; }
        var audit = scope.ServiceProvider.GetRequiredService<AuditService>();
        try {
            var schedule = await db.Schedules.AsSplitQuery().Include(x => x.Speakers).Include(x => x.Exceptions).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (schedule is null || !ScheduleClock.GetDueOccurrences(schedule, time.GetUtcNow(), TimeSpan.FromMinutes(5)).Contains(occurrence)) {
                run.Status = "Skipped";
                run.Message = "Schedule changed or was disabled before execution.";
            } else {
                audit.Add("Schedule", $"Triggered '{schedule.Name}' for {occurrence:O}.", id);
                await db.SaveChangesAsync(ct);
                var local = TimeZoneInfo.ConvertTime(occurrence, TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId));
                run.Message = await scope.ServiceProvider.GetRequiredService<SonosService>().PlaySelectionAsync(
                    schedule.Speakers.Where(x => x.Enabled).Select(x => (x.SpeakerId, x.Volume)).ToArray(),
                    schedule.GroupId, schedule.LibraryObjectId, schedule.SelectionMode, schedule.OnlyIfIdle, local, ct);
                run.Status = run.Message.StartsWith("Skipped", StringComparison.Ordinal) ? "Skipped" : "Completed";
            }
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            run.Status = "Interrupted";
            run.Message = "Application stopped during execution; this occurrence will not be retried.";
        } catch (Exception ex) {
            run.Status = "Failed";
            run.Message = ex.Message[..Math.Min(2000, ex.Message.Length)];
            await errors.RecordAsync(ex, "Schedule", cancellationToken: CancellationToken.None);
        }
        run.CompletedAt = time.GetUtcNow();
        audit.Add("Schedule", $"{run.Status}: {run.Message}", id, run.Status == "Failed" ? "Error" : "Information");
        await db.SaveChangesAsync(CancellationToken.None);
    }
}
