namespace AppServices;

public sealed class DatabaseStartupService(DbContextOptions<AppDbContext> options)
{
    public async Task<bool> HasPendingMigrationsAsync(CancellationToken ct)
    {
        await using var db = CreateStartupContext();
        return (await db.Database.GetPendingMigrationsAsync(ct)).Any();
    }

    public async Task MigrateAsync(CancellationToken ct)
    {
        await using var db = CreateStartupContext();
        await db.Database.MigrateAsync(ct);
    }

    private AppDbContext CreateStartupContext()
    {
        // SQL Server may still be starting when IIS preloads the application after a reboot.
        // Limit retries to initialization so playback and explicit transactions aren't replayed.
        var startupOptions = new DbContextOptionsBuilder<AppDbContext>(options)
            .UseSqlServer(sql => sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), [258]))
            .Options;
        return new AppDbContext(startupOptions);
    }
}
