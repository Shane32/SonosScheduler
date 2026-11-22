using Microsoft.Data.Sqlite;
using TestDb;

namespace Services;

public sealed class ScheduleRunTests
{
    [Theory]
    [InlineData(ScheduleRunStatus.Running, 0)]
    [InlineData(ScheduleRunStatus.Completed, 1)]
    [InlineData(ScheduleRunStatus.Skipped, 2)]
    [InlineData(ScheduleRunStatus.Interrupted, 3)]
    [InlineData(ScheduleRunStatus.Failed, 4)]
    public async Task StatusIsStoredAsAnIntegerAndRoundTrips(ScheduleRunStatus status, int storedValue)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new TestDbContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.ScheduleRuns.Add(new ScheduleRun { ScheduleId = 1, Status = status });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        using (var command = connection.CreateCommand()) {
            command.CommandText = "SELECT typeof(Status), Status FROM ScheduleRuns";
            using var reader = await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).ShouldBeTrue();
            reader.GetString(0).ShouldBe("integer");
            reader.GetInt32(1).ShouldBe(storedValue);
        }

        (await db.ScheduleRuns.SingleAsync()).Status.ShouldBe(status);
        (await db.ScheduleRuns.CountAsync(x => x.Status == status)).ShouldBe(1);
    }

    [Fact]
    public async Task DatabaseRejectsUndefinedStatusValues()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new TestDbContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.ScheduleRuns.Add(new ScheduleRun { ScheduleId = 1, Status = (ScheduleRunStatus)99 });

        var error = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());

        error.InnerException.ShouldBeOfType<SqliteException>().SqliteExtendedErrorCode.ShouldBe(275);
    }
}
