using AppServices;
using AppServices.Scheduling;
using AppServices.Sonos;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TestDb;

namespace Services;

public sealed class SonosServiceTests
{
    [Fact]
    public async Task WorkerClaimsOccurrenceOnceBeforePlayback()
    {
        using var fixture = new Fixture();
        await fixture.Db.SeedAsync<NetworkGroup>();
        var now = DateTimeOffset.Parse("2026-11-23T08:00:15Z", CultureInfo.InvariantCulture);
        var time = new FakeTimeProvider(now);
        fixture.Db.Schedules.Add(new Schedule {
            GroupId = 1,
            Name = "Morning",
            LocalTime = "08:00",
            TimeZoneId = "UTC",
            LibraryObjectId = "S:root",
            OnlyIfIdle = true,
            Speakers = [new() { SpeakerId = 1, Volume = 10 }]
        });
        await fixture.Db.SaveChangesAsync();
        var services = new ServiceCollection();
        services.AddSingleton<AppDbContext>(fixture.Db);
        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton(fixture.Service);
        services.AddSingleton(new AuditService(fixture.Db, time, new HttpContextAccessor()));
        services.AddSingleton<ILogger<ExceptionLogService>>(NullLogger<ExceptionLogService>.Instance);
        services.AddSingleton<ExceptionLogService>();
        using var provider = services.BuildServiceProvider();
        var worker = new ScheduleWorker(provider.GetRequiredService<IServiceScopeFactory>(), time, provider.GetRequiredService<ExceptionLogService>());
        await worker.TickAsync(CancellationToken.None);
        var count = fixture.Http.Enqueued.Count;
        count.ShouldBe(2);
        (await fixture.Db.ScheduleRuns.SingleAsync()).Status.ShouldBe("Completed");
        await worker.TickAsync(CancellationToken.None);
        fixture.Http.Enqueued.Count.ShouldBe(count);
        (await fixture.Db.ScheduleRuns.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task DiscoveryUsesCachedTopologyAndUpdatesOfflineRooms()
    {
        using var fixture = new Fixture();
        await fixture.Db.SeedAsync<NetworkGroup>();
        fixture.Http.OfflineOffice = true;
        await fixture.Service.RefreshAsync(1, CancellationToken.None);
        var kitchen = await fixture.Db.Speakers.SingleAsync(x => x.Id == 1);
        kitchen.IsOnline.ShouldBeTrue();
        kitchen.Volume.ShouldBe(22);
        (await fixture.Db.Speakers.SingleAsync(x => x.Id == 2)).IsOnline.ShouldBeFalse();
        fixture.Http.Calls.ShouldNotContain(x => x.Contains("192.168.1.1:", StringComparison.Ordinal));
        (await fixture.Db.Groups.SingleAsync()).LastRefreshedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task DiscoveryFallsBackToSubnetWhenNoCachedSpeakerExists()
    {
        using var fixture = new Fixture();
        fixture.Db.Groups.Add(new NetworkGroup { Id = 1, Name = "Single host", Subnet = "192.168.1.20/32" });
        await fixture.Db.SaveChangesAsync();
        await fixture.Service.RefreshAsync(1, CancellationToken.None);
        var speakers = await fixture.Db.Speakers.ToListAsync();
        speakers.ShouldHaveSingleItem();
        speakers[0].Address.ShouldBe("192.168.1.20");
        speakers[0].IsOnline.ShouldBeTrue();
    }

    [Fact]
    public async Task DaySelectionRecursesAndPreservesMetadataWhenQueued()
    {
        using var fixture = new Fixture();
        await fixture.Db.SeedAsync<NetworkGroup>();
        var result = await fixture.Service.PlaySelectionAsync([(1, 12)], 1, "S:root", "DayOfYearTrack", false,
            DateTimeOffset.Parse("2026-12-31T08:00:00-05:00", CultureInfo.InvariantCulture), CancellationToken.None);
        result.ShouldStartWith("Started 1 track");
        fixture.Http.Enqueued.ShouldBe(["x-file-cifs://nas/second.mp3"]);
        fixture.Http.Metadata.ShouldContain("Second");
        fixture.Http.Calls.ShouldContain(x => x.EndsWith("SetVolume", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IdlePolicyChecksLiveCoordinatorAndProtectsAllPlayingMembers()
    {
        using var fixture = new Fixture();
        await fixture.Db.SeedAsync<NetworkGroup>();
        fixture.Http.CombinedPlayingGroup = true;
        var result = await fixture.Service.PlaySelectionAsync([(1, 12), (2, 15)], 1, "S:root", "PlayAll", true,
            DateTimeOffset.UtcNow, CancellationToken.None);
        result.ShouldStartWith("Skipped");
        fixture.Http.Enqueued.ShouldBeEmpty();
        fixture.Http.Calls.ShouldNotContain(x => x.EndsWith("BecomeCoordinatorOfStandaloneGroup", StringComparison.Ordinal));
        fixture.Http.Calls.ShouldNotContain(x => x.EndsWith("SetVolume", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FolderPlaybackQueuesWholeSubtreeAndDelegatesUnselectedRooms()
    {
        using var fixture = new Fixture();
        await fixture.Db.SeedAsync<NetworkGroup>();
        fixture.Http.CombinedPlayingGroup = true;
        await fixture.Service.PlaySelectionAsync([(1, null)], 1, "S:root", "PlayAll", false, DateTimeOffset.UtcNow, CancellationToken.None);
        fixture.Http.Enqueued.ShouldBe(["x-file-cifs://nas/first.mp3", "x-file-cifs://nas/second.mp3"]);
        var delegateAt = fixture.Http.Calls.FindIndex(x => x.EndsWith("DelegateGroupCoordinationTo", StringComparison.Ordinal));
        var detachAt = fixture.Http.Calls.FindIndex(x => x.EndsWith("BecomeCoordinatorOfStandaloneGroup", StringComparison.Ordinal));
        delegateAt.ShouldBeGreaterThanOrEqualTo(0);
        detachAt.ShouldBeGreaterThan(delegateAt);
    }

    [Fact]
    public async Task RejectsOffSpeakerControlUrls()
    {
        using var fixture = new Fixture();
        fixture.Http.BadControlUrl = true;
        await Should.ThrowAsync<SonosException>(() => fixture.Client.GetSpeakerAsync(new Uri("http://192.168.1.20:1400/xml/device_description.xml"), CancellationToken.None));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private readonly HttpClient _httpClient;
        public TestDbContext Db { get; }
        public FakeSonosMessageHandler Http { get; } = new();
        public SonosClient Client { get; }
        public SonosService Service { get; }
        public Fixture()
        {
            _connection.Open();
            Db = new(_connection);
            Db.Database.EnsureCreated();
            _httpClient = new(Http);
            Client = new(_httpClient);
            var time = new FakeTimeProvider();
            Service = new(Db, Client, time, new AuditService(Db, time, new HttpContextAccessor()));
        }
        public void Dispose() { Db.Dispose(); _httpClient.Dispose(); _connection.Dispose(); }
    }
}
