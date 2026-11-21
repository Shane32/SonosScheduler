using Microsoft.EntityFrameworkCore.Design;

namespace AppDb;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer(Environment.GetEnvironmentVariable("ConnectionStrings__AppDbContext") ??
            "Server=(localdb)\\MSSQLLocalDB;Integrated Security=true;Database=SonosScheduler;").Options);
}
