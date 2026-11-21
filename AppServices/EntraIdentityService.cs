using System.Security.Claims;
using Microsoft.Extensions.Configuration;

namespace AppServices;

public sealed class EntraIdentityService(AppDbContext db, IConfiguration configuration)
{
    public async Task<bool> ApplyAsync(ClaimsIdentity identity, CancellationToken ct)
    {
        // The database is the only source of administrator permissions, including on every subsequent request.
        foreach (var claim in identity.Claims.Where(x => x.Type is ClaimTypes.Role or "roles" or "role").ToList())
            identity.RemoveClaim(claim);
        if (!Guid.TryParse(configuration["Entra:TenantId"], out var configuredTenant) || configuredTenant == Guid.Empty ||
            !Guid.TryParse(identity.FindFirst("tid")?.Value, out var tenantId) || tenantId != configuredTenant ||
            !Guid.TryParse(identity.FindFirst("oid")?.Value, out var objectId) || objectId == Guid.Empty ||
            !(identity.FindFirst("scp")?.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("access_as_user", StringComparer.Ordinal) ?? false))
            return false;
        var administrator = await db.Administrators.AsTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.ObjectId == objectId, ct);
        if (administrator != null) {
            identity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));
            // Display names are presentation metadata from this validated token, never permission keys.
            var displayName = identity.FindFirst("name")?.Value.Trim();
            if (!string.IsNullOrEmpty(displayName)) {
                if (displayName.Length > 200)
                    displayName = displayName[..(char.IsHighSurrogate(displayName[199]) ? 199 : 200)];
                if (!string.Equals(administrator.DisplayName, displayName, StringComparison.Ordinal)) {
                    administrator.DisplayName = displayName;
                    await db.SaveChangesAsync(ct);
                }
            }
        }
        return true;
    }
}
