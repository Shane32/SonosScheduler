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
