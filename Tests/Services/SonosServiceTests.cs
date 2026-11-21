using System.Net;
using System.Text;
using System.Xml.Linq;
using AppServices;
using AppServices.Scheduling;
using AppServices.Sonos;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TestDb;
using ProtocolSpeaker = AppServices.Sonos.Speaker;

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
        public FakeSonos Http { get; } = new();
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

    private sealed class FakeSonos : HttpMessageHandler
    {
        private static readonly string[] _services = ["AVTransport", "RenderingControl", "ZoneGroupTopology", "ContentDirectory"];
        public bool OfflineOffice { get; set; }
        public bool CombinedPlayingGroup { get; set; }
        public bool BadControlUrl { get; set; }
        public List<string> Calls { get; } = [];
        public List<string> Enqueued { get; } = [];
        public string Metadata { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var address = request.RequestUri!;
            if (OfflineOffice && address.Host.EndsWith(".21", StringComparison.Ordinal))
                throw new HttpRequestException("Offline");
            if (request.Method == HttpMethod.Get) {
                Calls.Add(address.ToString());
                var office = address.Host.EndsWith(".21", StringComparison.Ordinal);
                var name = office ? "Office" : "Kitchen";
                var services = _services.Select(type => new XElement("service", new XElement("serviceType", $"urn:schemas-upnp-org:service:{type}:1"), new XElement("controlURL", BadControlUrl ? "http://outside.example:1400/control" : $"/{type}/Control")));
                return Response(new XElement("root", new XElement("device", new XElement("UDN", $"uuid:RINCON_{name.ToUpperInvariant()}"), new XElement("roomName", name), new XElement("serviceList", services))).ToString());
            }
            var action = request.Headers.GetValues("SOAPACTION").Single().Trim('"').Split('#')[1];
            Calls.Add(address.Host + ":" + action);
            var body = XDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            string Arg(string name) => body.Descendants().First(x => x.Name.LocalName == name).Value;
            var values = new List<XElement>();
            switch (action) {
                case "GetZoneGroupState":
                    XElement Member(string name, string ip) => new("ZoneGroupMember", new XAttribute("UUID", "RINCON_" + name.ToUpperInvariant()), new XAttribute("ZoneName", name), new XAttribute("Location", $"http://{ip}:1400/xml/device_description.xml"));
                    var kitchen = Member("Kitchen", "192.168.1.20");
                    var office = Member("Office", "192.168.1.21");
                    var groups = CombinedPlayingGroup ? new XElement("ZoneGroups", new XElement("ZoneGroup", new XAttribute("Coordinator", "RINCON_KITCHEN"), kitchen, office)) : new XElement("ZoneGroups", new XElement("ZoneGroup", new XAttribute("Coordinator", "RINCON_KITCHEN"), kitchen), new XElement("ZoneGroup", new XAttribute("Coordinator", "RINCON_OFFICE"), office));
                    values.Add(new("ZoneGroupState", groups.ToString()));
                    break;
                case "GetTransportInfo":
                    values.Add(new("CurrentTransportState", CombinedPlayingGroup ? "PLAYING" : "STOPPED"));
                    break;
                case "GetPositionInfo":
                    values.Add(new("TrackMetaData", ""));
                    break;
                case "GetVolume":
                    values.Add(new("CurrentVolume", "22"));
                    break;
                case "GetMute":
                    values.Add(new("CurrentMute", "0"));
                    break;
                case "Browse":
                    var id = Arg("ObjectID");
                    var metadata = Arg("BrowseFlag") == "BrowseMetadata";
                    var items = metadata ? new[] { Item(id, "Folder", true) } : id == "S:root" ? new[] { Item("S:first", "First", false), Item("S:child", "Subfolder", true) } : new[] { Item("S:second", "Second", false) };
                    XNamespace didl = "urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/";
                    values.Add(new("Result", new XElement(didl + "DIDL-Lite", items).ToString()));
                    values.Add(new("NumberReturned", items.Length));
                    values.Add(new("TotalMatches", items.Length));
                    break;
                case "AddURIToQueue":
                    Enqueued.Add(Arg("EnqueuedURI"));
                    Metadata = Arg("EnqueuedURIMetaData");
                    values.Add(new("FirstTrackNumberEnqueued", Enqueued.Count));
                    break;
            }
            XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
            return Response(new XElement(soap + "Envelope", new XElement(soap + "Body", new XElement(action + "Response", values))).ToString());
        }
        private static XElement Item(string id, string title, bool folder)
        {
            XNamespace didl = "urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/";
            XNamespace dc = "http://purl.org/dc/elements/1.1/";
            return new XElement(didl + (folder ? "container" : "item"), new XAttribute("id", id), new XElement(dc + "title", title), folder ? null : new XElement(didl + "res", $"x-file-cifs://nas/{title.ToLowerInvariant()}.mp3"));
        }
        private static HttpResponseMessage Response(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "text/xml") };
    }
}
