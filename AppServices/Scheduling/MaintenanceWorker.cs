using AppServices.Sonos;
using Microsoft.Extensions.Hosting;

namespace AppServices.Scheduling;

public sealed class MaintenanceWorker(IServiceScopeFactory scopes, TimeProvider time, ExceptionLogService errors) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), time);
        var cleanupAt = DateTimeOffset.MinValue;
        do {
            try {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var ids = await db.Groups.Select(x => x.Id).ToArrayAsync(stoppingToken);
                await Task.WhenAll(ids.Select(id => RefreshAsync(id, stoppingToken)));
                if (time.GetUtcNow() >= cleanupAt) {
                    var cutoff = time.GetUtcNow().AddDays(-30);
                    await db.Logs.Where(x => x.Timestamp < cutoff).ExecuteDeleteAsync(stoppingToken);
                    await db.ScheduleRuns.Where(x => x.StartedAt < cutoff).ExecuteDeleteAsync(stoppingToken);
                    cleanupAt = time.GetUtcNow().AddDays(1);
                }
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; } catch (Exception ex) { await errors.RecordAsync(ex, "Maintenance", cancellationToken: CancellationToken.None); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RefreshAsync(int id, CancellationToken ct)
    {
        try {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<SonosService>().RefreshAsync(id, ct);
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) { } catch (Exception ex) { await errors.RecordAsync(ex, "Discovery", cancellationToken: CancellationToken.None); }
    }
}
