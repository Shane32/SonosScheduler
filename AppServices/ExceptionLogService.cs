using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AppServices;

public sealed class ExceptionLogService(IServiceScopeFactory scopes, TimeProvider time, ILogger<ExceptionLogService> logger)
{
    private static readonly Action<ILogger, Exception?> _failed = LoggerMessage.Define(LogLevel.Error, 1, "Could not persist application exception");
    public async Task RecordAsync(Exception exception, string category, string actor = "System", CancellationToken cancellationToken = default)
    {
        try {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Logs.Add(new LogEntry {
                Timestamp = time.GetUtcNow(),
                Level = "Error",
                Category = category,
                Message = exception.Message[..Math.Min(exception.Message.Length, 2000)],
                Actor = actor,
                Details = exception.ToString()
            });
            await db.SaveChangesAsync(cancellationToken);
        } catch (Exception ex) { _failed(logger, ex); }
    }
}
