using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace AppServer;

/// <summary>
/// Synchronizes validated token identities with database administrator permissions.
/// </summary>
public sealed class DbAuthService(ILogger<DbAuthService> logger)
{
    private static readonly Action<ILogger, string, Exception?> _logTokenValidationFailed = LoggerMessage.Define<string>(
        LogLevel.Error, new EventId(1, nameof(OnTokenValidatedAsync)), "Token validation failed for JWT subject: {JwtSubject}");

    public async Task OnTokenValidatedAsync(TokenValidatedContext context, CancellationToken cancellationToken)
    {
        if (context.Principal?.Identity is not ClaimsIdentity identity) {
            context.Fail("No valid claims identity found.");
            return;
        }

        // Only database grants determine application permissions, on every request.
        foreach (var claim in identity.Claims.Where(claim => claim.Type is "DbUserId" or ClaimTypes.Role or "roles" or "role").ToList())
            identity.RemoveClaim(claim);

        if (!Guid.TryParse(identity.FindFirst("tid")?.Value, out var tenantId) || tenantId == Guid.Empty ||
            !Guid.TryParse(identity.FindFirst("oid")?.Value, out var objectId) || objectId == Guid.Empty) {
            context.Fail("A tenant and object ID are required.");
            return;
        }
        if (Uri.TryCreate(context.Options.Authority, UriKind.Absolute, out var authority) &&
            Guid.TryParse(authority.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), out var configuredTenant) &&
            tenantId != configuredTenant) {
            context.Fail("The token tenant does not match the configured authority.");
            return;
        }

        try {
            var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var administrator = await db.Administrators.AsTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.ObjectId == objectId, cancellationToken);
            if (administrator is null)
                return;

            identity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));
            // Names are presentation metadata; immutable IDs determine permissions.
            var displayName = identity.FindFirst("name")?.Value.Trim();
            if (!string.IsNullOrEmpty(displayName)) {
                if (displayName.Length > 200)
                    displayName = displayName[..(char.IsHighSurrogate(displayName[199]) ? 199 : 200)];
                if (!string.Equals(administrator.DisplayName, displayName, StringComparison.Ordinal)) {
                    administrator.DisplayName = displayName;
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
        } catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested) {
            _logTokenValidationFailed(logger, $"{tenantId:D}/{objectId:D}", ex);
            context.Fail(ex);
        }
    }
}
