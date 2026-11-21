using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AppServices;

public sealed class AuditService(AppDbContext db, TimeProvider time, IHttpContextAccessor http)
{
    public string Actor
    {
        get {
            var user = http.HttpContext?.User;
            var tenantClaim = user?.FindFirst("tid")?.Value ?? user?.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
            var objectClaim = user?.FindFirst("oid")?.Value ?? user?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
            return user?.Identity?.IsAuthenticated == true && Guid.TryParse(tenantClaim, out var tenantId) && Guid.TryParse(objectClaim, out var objectId)
                ? $"{tenantId:D}/{objectId:D}"
                : http.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "System";
        }
    }
    public void Add(string category, string message, int? scheduleId = null, string level = "Information", string? details = null)
        => db.Logs.Add(new LogEntry {
            Timestamp = time.GetUtcNow(),
            Category = category,
            Message = message,
            Actor = Actor,
            ScheduleId = scheduleId,
            Level = level,
            Details = details
        });
}

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
