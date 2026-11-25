using AppServices.Sonos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Services;

namespace Mutations;

public sealed class SonosSchedules : TestBase
{
    private readonly FakeSonosMessageHandler _sonos = new();

    protected override void ConfigureWebHostBuilder(IWebHostBuilder webHostBuilder)
    {
        base.ConfigureWebHostBuilder(webHostBuilder);
        webHostBuilder.ConfigureTestServices(services =>
            services.AddHttpClient<SonosClient>().ConfigurePrimaryHttpMessageHandler(() => _sonos));
    }

    [Fact]
    public async Task TestRunsSavedScheduleWithoutClaimingAnOccurrence()
    {
        await Db.SeedAsync<NetworkGroup>();
        var now = TimeProvider.GetUtcNow();
        var schedule = new Schedule {
            GroupId = 1,
            Name = "Manual playback",
            TimeZoneId = "America/New_York",
            Enabled = false,
            DisabledUntil = now.AddDays(3),
            UpdatedAt = now.AddDays(-1),
            LibraryObjectId = "S:root",
            SelectionMode = "DayOfYearTrack",
            OnlyIfIdle = true,
            Speakers = [new() { SpeakerId = 1, Volume = 15 }, new() { SpeakerId = 2, Volume = 25, Enabled = false }],
            Exceptions = [new() { Date = "2024-12-31", Reason = "Holiday" }]
        };
        Db.Schedules.Add(schedule);
        await Db.SaveChangesAsync();

        var response = await RunQueryAsync($$"""mutation { schedules { test(id: "{{schedule.Id}}") } }""");

        response.ShouldMatchApproved();
        _sonos.Enqueued.ShouldBe(["x-file-cifs://nas/first.mp3", "x-file-cifs://nas/second.mp3", "x-file-cifs://nas/third.mp3"]);
        _sonos.SeekTargets.ShouldBe(["3"]);
        _sonos.Calls.Where(x => x.EndsWith(":SetVolume", StringComparison.Ordinal))
            .ShouldBe(["192.168.1.20:SetVolume"]);
        (await Db.ScheduleRuns.CountAsync()).ShouldBe(0);
        var saved = await Db.Schedules.SingleAsync();
        saved.Enabled.ShouldBeFalse();
        saved.DisabledUntil.ShouldBe(schedule.DisabledUntil);
        saved.UpdatedAt.ShouldBe(schedule.UpdatedAt);
        (await Db.Logs.AnyAsync(x => x.ScheduleId == schedule.Id && x.Message == "Manual test: Started playback at track 3 of 3 in Kitchen.")).ShouldBeTrue();
    }

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
