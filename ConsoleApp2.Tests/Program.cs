using System.Net;
using System.Text;
using System.Xml.Linq;
using SonosController;

// Dependency-free protocol checks. No real speakers or network calls are used.
var tests = new (string Name, Func<Task> Run)[]
{
    ("settings validation and address normalization", Settings),
    ("IPv4 subnet validation, normalization, and host boundaries", SubnetRanges),
    ("subnet scan filters non-Sonos hosts and tolerates probe failures", SubnetDiscovery),
    ("subnet scan retains explicit seeds without duplicate probes", SubnetSeeds),
    ("subnet scan respects its concurrency limit", SubnetConcurrency),
    ("subnet probe timeout and user cancellation", SubnetCancellation),
    ("seed discovery expands topology and hides satellites", Discovery),
    ("grouped status uses coordinator and selected speaker volume", Status),
    ("library paging and complete escaped metadata", Browse),
    ("playback appends, selects the correct queue, seeks and plays", Playback),
    ("SOAP faults, missing services, and invalid HTTP responses", Errors),
    ("timeouts and cancellation propagate", Cancellation),
    ("console status and quitting from selected speaker", MenuStatus),
    ("console browsing preserves actual page boundaries and parent folder", MenuPaging),
    ("console search preserves case and starts selected song", MenuSearch)
};
var failures = 0;
foreach (var test in tests) {
    try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); } catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {test.Name}: {ex}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} checks passed.");
return failures == 0 ? 0 : 1;

static Task Settings()
{
    new SonosSettings().Validate();
    Equal("http://192.168.1.50:1400/xml/device_description.xml", SonosSettings.DescriptionUri("192.168.1.50").AbsoluteUri);
    Equal("http://speaker:1500/description.xml", SonosSettings.DescriptionUri("http://speaker:1500/description.xml").AbsoluteUri);
    Throws<InvalidDataException>(() => new SonosSettings { LibraryPageSize = 0 }.Validate());
    Throws<InvalidDataException>(() => new SonosSettings { DiscoveryTimeoutSeconds = 0 }.Validate());
    Throws<InvalidDataException>(() => new SonosSettings { HttpTimeoutSeconds = 121 }.Validate());
    Throws<InvalidDataException>(() => new SonosSettings { DiscoveryInterfaceAddress = "::1" }.Validate());
    Throws<InvalidDataException>(() => new SonosSettings { SpeakerAddresses = ["https://speaker"] }.Validate());
    Throws<InvalidDataException>(() => new SonosSettings { SpeakerAddresses = [""] }.Validate());
    new SonosSettings { DiscoverySubnet = "192.0.2.123/24" }.Validate();
    new SonosSettings { DiscoverySubnet = " " }.Validate();
    Throws<InvalidDataException>(() => new SonosSettings { DiscoverySubnet = "192.0.2.0/33" }.Validate());
    Throws<InvalidDataException>(() => new SonosSettings { SubnetScanConcurrency = 0 }.Validate());
    Throws<InvalidDataException>(() => new SonosSettings { SubnetScanConcurrency = 257 }.Validate());
    Throws<InvalidDataException>(() => new SonosSettings { SubnetScanTimeoutSeconds = 0 }.Validate());
    Throws<InvalidDataException>(() => new SonosSettings { SubnetScanTimeoutSeconds = 31 }.Validate());
    return Task.CompletedTask;
}

static Task SubnetRanges()
{
    var subnet = IPv4Subnet.Parse("192.168.50.123/24");
    Equal("192.168.50.0/24", subnet.ToString());
    Equal(254, subnet.HostCount);
    Equal("192.168.50.1", subnet.GetHostAddresses().First().ToString());
    Equal("192.168.50.254", subnet.GetHostAddresses().Last().ToString());
    Equal(65534, IPv4Subnet.Parse("192.168.42.12/16").HostCount);
    Equal("255.255.255.254,255.255.255.255", string.Join(',', IPv4Subnet.Parse("255.255.255.255/31").GetHostAddresses()));
    Equal("255.255.255.255", IPv4Subnet.Parse("255.255.255.255/32").GetHostAddresses().Single().ToString());
    foreach (var invalid in new[] { "", "192.168.1.0", "192.168.1.0/15", "192.168.1.0/-1", "192.168.1.0/33", "192.168.256.0/24", "192.168.1/24", "::1/32", "hostname/24", "192.168.1.0/24/24" })
        Throws<InvalidDataException>(() => IPv4Subnet.Parse(invalid));
    return Task.CompletedTask;
}

static async Task SubnetDiscovery()
{
    using var fixture = new Fixture();
    fixture.Override = request => request.Method == "GET" ? request.Host switch {
        "192.0.2.1" => null, // A real Sonos description from the default fixture.
        "192.0.2.2" => new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent("<root><device><UDN>uuid:OTHER_DEVICE</UDN></device></root>")
        },
        "192.0.2.3" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not XML") },
        "192.0.2.4" => new HttpResponseMessage(HttpStatusCode.NotFound),
        "192.0.2.5" => throw new TaskCanceledException("Probe timeout"),
        _ => throw new HttpRequestException("Connection refused")
    } : request.Action == "GetZoneGroupState" ? Fixture.StandaloneTopologyResponse(request.Host) : null;
    var logs = new List<string>();
    var settings = new SonosSettings { DiscoverySubnet = "192.0.2.0/29" };
    var speakers = await new SonosDiscovery(fixture.Client, settings, logs.Add).FindSpeakersAsync(default);
    Equal("RINCON_192.0.2.1", speakers.Single().Id);
    Equal("192.0.2.1,192.0.2.2,192.0.2.3,192.0.2.4,192.0.2.5,192.0.2.6",
        string.Join(',', fixture.Requests.Where(r => r.Method == "GET").Select(r => r.Host).Order()));
    Equal(2, logs.Count); // Start and summary only: failed probes must not flood the console.
    Check(logs.Last().Contains("6/6 addresses checked; 1 Sonos device(s) found"), "Scan summary missing.");
}

static async Task SubnetSeeds()
{
    using var fixture = new Fixture();
    fixture.Override = request => request.Action == "GetZoneGroupState" ? Fixture.StandaloneTopologyResponse(request.Host) : null;
    var settings = new SonosSettings { SpeakerAddresses = ["192.0.2.1"], DiscoverySubnet = "192.0.2.1/32" };
    var speakers = await new SonosDiscovery(fixture.Client, settings, _ => { }).FindSpeakersAsync(default);
    Equal("RINCON_192.0.2.1", speakers.Single().Id);
    Equal(1, fixture.Requests.Count(r => r.Method == "GET"));
}

static async Task SubnetConcurrency()
{
    using var fixture = new Fixture();
    var firstWave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var active = 0;
    var overlapped = false;
    fixture.OverrideAsync = async (request, ct) => {
        if (request.Method != "GET")
            return null;
        var count = Interlocked.Increment(ref active);
        try {
            Check(count <= 2, "Too many concurrent probes.");
            if (count == 2) { overlapped = true; firstWave.TrySetResult(); }
            await firstWave.Task.WaitAsync(ct);
            await Task.Yield();
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        } finally { Interlocked.Decrement(ref active); }
    };
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var settings = new SonosSettings { DiscoverySubnet = "192.0.2.0/29", SubnetScanConcurrency = 2 };
    var speakers = await new SonosDiscovery(fixture.Client, settings, _ => { }).FindSpeakersAsync(deadline.Token);
    Equal(0, speakers.Count);
    Equal(6, fixture.Requests.Count);
    Check(overlapped, "Probes ran sequentially instead of concurrently.");
}

static async Task SubnetCancellation()
{
    using var fixture = new Fixture();
    fixture.OverrideAsync = async (_, ct) => {
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        throw new InvalidOperationException("Probe was not cancelled.");
    };
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var settings = new SonosSettings { DiscoverySubnet = "192.0.2.1/32", SubnetScanTimeoutSeconds = 1 };
    var speakers = await new SonosDiscovery(fixture.Client, settings, _ => { }).FindSpeakersAsync(deadline.Token);
    Equal(0, speakers.Count); // The per-probe timeout recovers rather than aborting discovery.
    using var cancelled = new CancellationTokenSource();
    fixture.OverrideAsync = async (_, ct) => {
        cancelled.Cancel();
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        throw new InvalidOperationException("User cancellation was ignored.");
    };
    await ThrowsAsync<OperationCanceledException>(() =>
        new SonosDiscovery(fixture.Client, settings, _ => { }).FindSpeakersAsync(cancelled.Token));
}

static async Task Discovery()
{
    using var fixture = new Fixture();
    var logs = new List<string>();
    var discovery = new SonosDiscovery(fixture.Client, new SonosSettings { SpeakerAddresses = ["kitchen"] }, logs.Add);
    var speakers = await discovery.FindSpeakersAsync(default);
    Equal("Kitchen,Living room", string.Join(',', speakers.Select(s => s.Name)));
    Equal(0, logs.Count);
    Check(fixture.Requests.Any(r => r.Host == "living" && r.Method == "GET"), "Coordinator description not loaded.");
    Check(!fixture.Requests.Any(r => r.Host == "sub" && r.Method == "GET"), "Invisible satellite should not be loaded.");
}

static async Task Status()
{
    using var fixture = new Fixture();
    var selected = await fixture.Client.GetSpeakerAsync(SonosSettings.DescriptionUri("kitchen"), default);
    var target = await fixture.Client.ResolveTargetAsync(selected, default);
    Equal("RINCON_LIVING", target.Coordinator.Id);
    Equal("Living room,Kitchen", string.Join(',', target.Rooms));
    var status = await fixture.Client.GetStatusAsync(selected, target.Coordinator, default);
    Equal("PLAYING", status.State);
    Equal("Rock & Roll <Live>", status.Title);
    Equal("A & B", status.Artist);
    Equal("The Album", status.Album);
    Equal("0:01:02", status.Position);
    Equal("0:03:45", status.Duration);
    Equal("28", status.Volume);
    Check(status.Muted, "Mute status not parsed.");
    Check(fixture.Requests.Where(r => r.Action is "GetTransportInfo" or "GetPositionInfo").All(r => r.Host == "living"), "Transport routed to wrong room.");
    Check(fixture.Requests.Where(r => r.Action is "GetVolume" or "GetMute").All(r => r.Host == "kitchen"), "Volume routed to wrong room.");
}

static async Task Browse()
{
    using var fixture = new Fixture();
    var speaker = await fixture.Client.GetSpeakerAsync(SonosSettings.DescriptionUri("living"), default);
    var page = await fixture.Client.BrowseAsync(speaker, "A:TRACKS:Rock%20%26%20Roll", 25, 25, default);
    Equal(1, page.Items.Count);
    Equal(1, page.NumberReturned); // Sonos can return fewer results than requested.
    Equal(30, page.TotalMatches);
    Equal("25", fixture.Requests.Last().Arguments["StartingIndex"]);
    Equal("25", fixture.Requests.Last().Arguments["RequestedCount"]);
    Equal("A:TRACKS:Rock%20%26%20Roll", fixture.Requests.Last().Arguments["ObjectID"]);
    var item = page.Items[0];
    Check(!item.IsContainer, "Song parsed as container.");
    Equal("x-file-cifs://nas/Music/Rock%20%26%20Roll.flac", item.ResourceUri);
    var metadata = XDocument.Parse(item.Metadata);
    Equal("Rock & Roll <Live>", metadata.Descendants().Single(e => e.Name.LocalName == "title").Value);
    Equal("keep me", metadata.Descendants().Single(e => e.Name.LocalName == "custom").Value);
    var container = LibraryItem.FromXml(XElement.Parse("<container id='A:ALBUM/1'><title xmlns='http://purl.org/dc/elements/1.1/'>Album</title></container>"));
    Check(container.IsContainer, "Container parsed as song.");
    await fixture.Client.BrowseAsync(speaker, "A:", 26, 25, default);
    Equal("26", fixture.Requests.Last().Arguments["StartingIndex"]);
}

static async Task Playback()
{
    using var fixture = new Fixture();
    var selected = await fixture.Client.GetSpeakerAsync(SonosSettings.DescriptionUri("kitchen"), default);
    var target = await fixture.Client.ResolveTargetAsync(selected, default);
    var item = (await fixture.Client.BrowseAsync(target.Coordinator, "A:TRACKS", 0, 25, default)).Items[0];
    fixture.Requests.Clear();
    await fixture.Client.PlayLibraryItemAsync(target.Coordinator, item, default);
    Equal("AddURIToQueue,SetAVTransportURI,Seek,Play", string.Join(',', fixture.Requests.Select(r => r.Action)));
    Check(fixture.Requests.All(r => r.Host == "living"), "Playback did not use coordinator.");
    Equal("0", fixture.Requests[0].Arguments["DesiredFirstTrackNumberEnqueued"]);
    Equal("0", fixture.Requests[0].Arguments["EnqueueAsNext"]);
    Equal(item.ResourceUri, fixture.Requests[0].Arguments["EnqueuedURI"]);
    Equal(item.Metadata, fixture.Requests[0].Arguments["EnqueuedURIMetaData"]);
    Equal("x-rincon-queue:RINCON_LIVING#0", fixture.Requests[1].Arguments["CurrentURI"]);
    Equal("TRACK_NR", fixture.Requests[2].Arguments["Unit"]);
    Equal("7", fixture.Requests[2].Arguments["Target"]);
    Equal("1", fixture.Requests[3].Arguments["Speed"]);
    await fixture.Client.PauseAsync(target.Coordinator, default);
    Equal("Pause", fixture.Requests.Last().Action);
    await ThrowsAsync<SonosException>(() => fixture.Client.PlayLibraryItemAsync(target.Coordinator, item with { IsContainer = true }, default));
}

static async Task Errors()
{
    using var fixture = new Fixture();
    var speaker = await fixture.Client.GetSpeakerAsync(SonosSettings.DescriptionUri("living"), default);
    fixture.Override = _ => Fixture.Fault("701", "No such object");
    var empty = await fixture.Client.BrowseAsync(speaker, "A:TRACKS:missing", 0, 25, default);
    Equal(0, empty.TotalMatches);
    fixture.Override = _ => Fixture.Fault("800", "Command not supported & rejected");
    var error = await ThrowsAsync<SonosException>(() => fixture.Client.PauseAsync(speaker, default));
    Equal("800", error.ErrorCode);
    Check(error.Message.Contains("Command not supported & rejected"), "SOAP error description lost.");
    fixture.Override = _ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("not XML") };
    var invalid = await ThrowsAsync<SonosException>(() => fixture.Client.ResumeAsync(speaker, default));
    Check(invalid.Message.Contains("502"), "HTTP status lost.");
    fixture.Override = null;
    await ThrowsAsync<SonosException>(() => fixture.Client.PauseAsync(speaker with { Services = new Dictionary<string, SonosService>() }, default));
}

static async Task Cancellation()
{
    using var fixture = new Fixture();
    var speaker = await fixture.Client.GetSpeakerAsync(SonosSettings.DescriptionUri("living"), default);
    fixture.Override = _ => throw new TaskCanceledException("Simulated HTTP timeout");
    await ThrowsAsync<OperationCanceledException>(() => fixture.Client.ResumeAsync(speaker, default));
    Check(SonosDiscovery.IsConnectionError(new TaskCanceledException(), default), "HTTP timeout should be recoverable.");
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    Check(!SonosDiscovery.IsConnectionError(new TaskCanceledException(), cts.Token), "User cancellation must propagate.");
    await ThrowsAsync<OperationCanceledException>(() => fixture.Client.GetSpeakerAsync(SonosSettings.DescriptionUri("living"), cts.Token));
}

static async Task MenuStatus()
{
    using var fixture = new Fixture();
    var output = await RunMenuAsync(fixture, "1\n1\nq\n");
    Check(output.Contains("Song: Rock & Roll <Live>"), "Status was not displayed.");
    Equal(1, output.Split("Finding speakers...").Length - 1);
    Check(!fixture.Requests.Any(r => r.Action is "Play" or "Pause"), "Viewing status changed playback.");
}

static async Task MenuPaging()
{
    using var fixture = new Fixture();
    fixture.Override = request => request.Action != "Browse" ? null :
        request.Arguments["ObjectID"] == "A:"
            ? Fixture.LibraryResponse("<DIDL-Lite xmlns='urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/'><container id='A:TRACKS'><title xmlns='http://purl.org/dc/elements/1.1/'>Tracks</title></container></DIDL-Lite>", 1, 1)
            : Fixture.LibraryResponse(Fixture.Didl, 1, 3);
    await RunMenuAsync(fixture, "1\n2\n1\nn\nn\np\nb\nb\nq\n");
    Equal("A:@0,A:TRACKS@0,A:TRACKS@1,A:TRACKS@2,A:TRACKS@1,A:@0",
        string.Join(',', fixture.Requests.Where(r => r.Action == "Browse").Select(r => $"{r.Arguments["ObjectID"]}@{r.Arguments["StartingIndex"]}")));
    Check(!fixture.Requests.Any(r => r.Action is "Play" or "Pause"), "Browsing changed playback.");
}

static async Task MenuSearch()
{
    using var fixture = new Fixture();
    var output = await RunMenuAsync(fixture, "1\n3\nRock & Roll\n1\nq\n");
    Equal("A:TRACKS:Rock%20%26%20Roll", fixture.Requests.Single(r => r.Action == "Browse").Arguments["ObjectID"]);
    Check(output.Contains("Playing 'Rock & Roll <Live>' in Living room, Kitchen"), "Selected song did not play.");
    Equal(1, fixture.Requests.Count(r => r.Action == "Play"));
}

static async Task<string> RunMenuAsync(Fixture fixture, string input)
{
    var previousIn = Console.In;
    var previousOut = Console.Out;
    using var writer = new StringWriter();
    using var reader = new StringReader(input);
    try {
        Console.SetIn(reader);
        Console.SetOut(writer);
        var settings = new SonosSettings { SpeakerAddresses = ["kitchen"], LibraryPageSize = 2 };
        await new ConsoleMenu(fixture.Client, new SonosDiscovery(fixture.Client, settings, _ => { }), settings).RunAsync(default);
        return writer.ToString();
    } finally { Console.SetIn(previousIn); Console.SetOut(previousOut); }
}

static void Check(bool value, string message)
{
    if (!value)
        throw new InvalidOperationException(message);
}
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
}
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T ex) { return ex; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
