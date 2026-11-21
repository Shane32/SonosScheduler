using System.Data;
using Microsoft.Extensions.Configuration;

namespace AppServices;

public sealed class AdminService(AppDbContext db, AuditService audit, IConfiguration configuration)
{
    public async Task BootstrapAsync(CancellationToken ct)
    {
        if (await db.Administrators.AnyAsync(ct))
            return;
        var configuredObjectId = configuration["Entra:BootstrapAdminObjectId"];
        if (string.IsNullOrWhiteSpace(configuredObjectId))
            return;
        if (!Guid.TryParse(configuredObjectId, out var objectId))
            throw new InvalidOperationException("The bootstrap administrator object ID must be a valid GUID.");
        await AddAsync(objectId, ct);
    }

    public async Task<AdministratorGrant> AddAsync(Guid objectId, CancellationToken ct, string? displayName = null)
    {
        if (objectId == Guid.Empty)
            throw new ClientError("An administrator object ID is required.");
        displayName = displayName?.Trim() ?? "";
        if (displayName.Length > 200)
            throw new ClientError("An administrator display name cannot exceed 200 characters.");
        if (!Guid.TryParse(configuration["Entra:TenantId"], out var tenantId) || tenantId == Guid.Empty)
            throw new InvalidOperationException("An Entra tenant ID must be configured before granting administrator access.");
        if (await db.Administrators.AnyAsync(x => x.TenantId == tenantId && x.ObjectId == objectId, ct))
            throw new ClientError("This Entra account is already an administrator.");
        var admin = new AdministratorGrant { TenantId = tenantId, ObjectId = objectId, DisplayName = displayName };
        db.Administrators.Add(admin);
        audit.Add("Admin", $"Granted administrator access to Entra object '{objectId:D}' in tenant '{tenantId:D}'.");
        await db.SaveChangesAsync(ct);
        return admin;
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (await db.Administrators.CountAsync(ct) <= 1)
            throw new ClientError("At least one administrator must remain.");
        var admin = await db.Administrators.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ClientError("Administrator not found.");
        db.Administrators.Remove(admin);
        audit.Add("Admin", $"Revoked administrator access from Entra object '{admin.ObjectId:D}' in tenant '{admin.TenantId:D}'.");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
