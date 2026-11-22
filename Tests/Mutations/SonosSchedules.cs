namespace Mutations;

public sealed class SonosSchedules : TestBase
{
    [Fact]
    public async Task CreateWithBrowserIanaAndWindowsTimeZones()
    {
        await Db.SeedAsync<NetworkGroup>();
        var response = await RunQueryAsync("""
            mutation {
              iana: schedules {
                add(input: {
                  groupId: "1", name: "Browser time zone", daysOfWeek: 127, localTime: "08:00", timeZoneId: "America/New_York",
                  enabled: true, onlyIfIdle: true, libraryObjectId: "S:folder", libraryTitle: "Morning folder", selectionMode: "PlayAll",
                  speakers: [{speakerId: "1", enabled: true}], exceptions: []
                }) { id name timeZoneId }
              }
              windows: schedules {
                add(input: {
                  groupId: "1", name: "Windows time zone", daysOfWeek: 127, localTime: "08:00", timeZoneId: "Eastern Standard Time",
                  enabled: true, onlyIfIdle: true, libraryObjectId: "S:folder", libraryTitle: "Morning folder", selectionMode: "PlayAll",
                  speakers: [{speakerId: "1", enabled: true}], exceptions: []
                }) { id name timeZoneId }
              }
            }
            """);

        response.ShouldMatchApproved();
        (await Db.Schedules.OrderBy(x => x.Id).Select(x => x.TimeZoneId).ToArrayAsync()).ShouldBe(["America/New_York", "Eastern Standard Time"]);
    }

    [Fact]
    public async Task CreateEditPauseAndDelete()
    {
        await Db.SeedAsync<NetworkGroup>();
        var response = await RunQueryAsync("""
            mutation {
              schedules { add(input: {
                groupId: "1", name: "Morning", daysOfWeek: 62, localTime: "08:00", timeZoneId: "America/New_York",
                enabled: true, onlyIfIdle: true, libraryObjectId: "S:folder", libraryTitle: "Morning folder", selectionMode: "RandomTrack",
                speakers: [{speakerId: "1", enabled: true, volume: 15}, {speakerId: "2", enabled: false}],
                exceptions: [{date: "2026-12-25", reason: "Holiday"}]
              }) { id name localTime timeZoneId speakers { speakerId enabled volume } exceptions { date reason } } }
            }
            """);
        response.ShouldMatchApproved();
        var schedule = await Db.Schedules.SingleAsync();
        (await Db.Logs.CountAsync(x => x.Category == "ScheduleEdit")).ShouldBe(1);
        await RunQueryAsync($$"""
            mutation { schedules { update(id: "{{schedule.Id}}", input: {
              groupId: "1", name: "Evening", daysOfWeek: 127, localTime: "18:00", timeZoneId: "UTC",
              enabled: true, onlyIfIdle: false, libraryObjectId: "S:evening", libraryTitle: "Evening", selectionMode: "PlayAll",
              speakers: [{speakerId: "2", enabled: true}], exceptions: []
            }) { id } } }
            """);
        var updated = await Db.Schedules.Include(x => x.Speakers).Include(x => x.Exceptions).SingleAsync();
        updated.Name.ShouldBe("Evening");
        updated.Speakers.ShouldHaveSingleItem().SpeakerId.ShouldBe(2);
        updated.Exceptions.ShouldBeEmpty();
        await RunQueryAsync($$"""mutation { schedules { disableFor(id: "{{schedule.Id}}", days: 3) } }""");
        (await Db.Schedules.SingleAsync()).DisabledUntil.ShouldBe(TimeProvider.GetUtcNow().AddDays(3));
        await RunQueryAsync($$"""mutation { schedules { setEnabled(id: "{{schedule.Id}}", enabled: true) } }""");
        (await Db.Schedules.SingleAsync()).DisabledUntil.ShouldBeNull();
        await RunQueryAsync($$"""mutation { schedules { delete(id: "{{schedule.Id}}") } }""");
        (await Db.Schedules.CountAsync()).ShouldBe(0);
        (await Db.Logs.CountAsync(x => x.Category == "ScheduleEdit")).ShouldBe(5);
    }
}
