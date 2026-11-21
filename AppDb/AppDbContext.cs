namespace AppDb;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<NetworkGroup> Groups => Set<NetworkGroup>();
    public DbSet<Speaker> Speakers => Set<Speaker>();
    public DbSet<Schedule> Schedules => Set<Schedule>();
    public DbSet<ScheduleSpeaker> ScheduleSpeakers => Set<ScheduleSpeaker>();
    public DbSet<ScheduleExclusion> ScheduleExclusions => Set<ScheduleExclusion>();
    public DbSet<ScheduleRun> ScheduleRuns => Set<ScheduleRun>();
    public DbSet<AdministratorGrant> Administrators => Set<AdministratorGrant>();
    public DbSet<LogEntry> Logs => Set<LogEntry>();
    public static string? Schema => null; //set to a string like "app" to use a specific schema

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (Schema != null)
            modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Speaker>().HasOne(x => x.Group).WithMany(x => x.Speakers).HasForeignKey(x => x.GroupId);
        modelBuilder.Entity<Schedule>().HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ScheduleSpeaker>().HasOne(x => x.Schedule).WithMany(x => x.Speakers).HasForeignKey(x => x.ScheduleId);
        modelBuilder.Entity<ScheduleSpeaker>().HasOne(x => x.Speaker).WithMany().HasForeignKey(x => x.SpeakerId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ScheduleExclusion>().HasOne(x => x.Schedule).WithMany(x => x.Exceptions).HasForeignKey(x => x.ScheduleId);
    }
}
