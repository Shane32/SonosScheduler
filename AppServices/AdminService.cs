using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace AppServices;

public sealed class AdminService(AppDbContext db, AuditService audit)
{
    private readonly PasswordHasher<AdminUser> _hasher = new();

    public async Task BootstrapAsync(IConfiguration configuration, CancellationToken ct)
    {
        if (await db.Admins.AnyAsync(ct))
            return;
        var name = configuration["Admin:BootstrapName"];
        var password = configuration["Admin:BootstrapPassword"];
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(password))
            return;
        await AddAsync(name, password, ct);
    }

    public async Task<AdminUser> AddAsync(string name, string password, CancellationToken ct)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 100 || password.Length is < 12 or > 200)
            throw new ClientError("Administrator names require 1–100 characters; passwords require 12–200 characters.");
        var normalized = name.ToUpperInvariant();
        if (await db.Admins.AnyAsync(x => x.NormalizedName == normalized, ct))
            throw new ClientError("An administrator with this name already exists.");
        var admin = new AdminUser { Name = name, NormalizedName = normalized };
        admin.PasswordHash = _hasher.HashPassword(admin, password);
        db.Admins.Add(admin);
        audit.Add("Admin", $"Added administrator '{name}'.");
        await db.SaveChangesAsync(ct);
        return admin;
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (await db.Admins.CountAsync(ct) <= 1)
            throw new ClientError("At least one administrator must remain.");
        var admin = await db.Admins.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ClientError("Administrator not found.");
        db.Admins.Remove(admin);
        audit.Add("Admin", $"Removed administrator '{admin.Name}'.");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<AdminUser?> ValidateAsync(string name, string password, CancellationToken ct)
    {
        if (name.Length > 100 || password.Length > 200)
            return null;
        var normalized = name.Trim().ToUpperInvariant();
        var admin = await db.Admins.SingleOrDefaultAsync(x => x.NormalizedName == normalized, ct);
        if (admin is null) { _ = _hasher.HashPassword(new AdminUser(), password); return null; }
        var result = _hasher.VerifyHashedPassword(admin, admin.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
            return null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded) {
            admin.PasswordHash = _hasher.HashPassword(admin, password);
            await db.SaveChangesAsync(ct);
        }
        return admin;
    }
}
