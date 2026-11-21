using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml;

namespace SonosController;

public sealed class SonosDiscovery(SonosClient client, SonosSettings settings, Action<string> log)
{
    public async Task<IReadOnlyList<Speaker>> FindSpeakersAsync(CancellationToken ct)
    {
        // Configured addresses act as seeds; their topology can reveal the remaining rooms.
        var locations = settings.SpeakerAddresses.Select(SonosSettings.DescriptionUri).ToHashSet();
        if (locations.Count == 0 && string.IsNullOrWhiteSpace(settings.DiscoverySubnet))
        {
            foreach (var location in await DiscoverLocationsAsync(ct)) locations.Add(location);
        }
        var speakers = new ConcurrentDictionary<string, Speaker>();
        var hidden = new HashSet<string>();
        await LoadSpeakersAsync(locations, speakers, ct);
        if (!string.IsNullOrWhiteSpace(settings.DiscoverySubnet))
            await ScanSubnetAsync(IPv4Subnet.Parse(settings.DiscoverySubnet), speakers, ct);
        var extraLocations = new HashSet<Uri>();
        foreach (var seed in speakers.Values.ToArray())
        {
            try
            {
                foreach (var member in (await client.GetGroupsAsync(seed, ct)).SelectMany(g => g.Members))
                {
                    if (!member.Visible) hidden.Add(member.Id);
                    else if (!speakers.ContainsKey(member.Id)) extraLocations.Add(member.DescriptionUri);
                }
                // One successful topology response is enough for this household. Keep looking at other
                // seeds too, since the LAN may have separate S1/S2 households.
            }
            catch (Exception ex) when (IsConnectionError(ex, ct))
            {
                log($"Could not read topology from {seed.Name}: {ex.Message}");
            }
        }
        await LoadSpeakersAsync(extraLocations, speakers, ct);
        return speakers.Values.Where(s => !hidden.Contains(s.Id) && s.Services.ContainsKey("AVTransport"))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task ScanSubnetAsync(IPv4Subnet subnet, ConcurrentDictionary<string, Speaker> speakers, CancellationToken ct)
    {
        log($"Scanning {subnet}: {subnet.HostCount:N0} addresses on TCP 1400 (up to {settings.SubnetScanConcurrency} at a time). Ctrl+C to cancel.");
        var knownLocations = speakers.Values.Select(s => s.DescriptionUri).ToHashSet();
        var completed = 0;
        var found = 0;
        await Parallel.ForEachAsync(subnet.GetHostAddresses(), new ParallelOptions
        {
            MaxDegreeOfParallelism = settings.SubnetScanConcurrency,
            CancellationToken = ct
        }, async (address, token) =>
        {
            var location = SonosSettings.DescriptionUri(address.ToString());
            if (!knownLocations.Contains(location))
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(settings.SubnetScanTimeoutSeconds));
                try
                {
                    var speaker = await client.GetSpeakerAsync(location, timeout.Token);
                    if (speaker.Id.StartsWith("RINCON_", StringComparison.OrdinalIgnoreCase) &&
                        speaker.Services.ContainsKey("AVTransport") && speaker.Services.ContainsKey("ZoneGroupTopology"))
                    {
                        speakers[speaker.Id] = speaker;
                        Interlocked.Increment(ref found);
                    }
                }
                catch (Exception ex) when (IsConnectionError(ex, token))
                {
                    // Most addresses will be offline or not Sonos devices. Keep those misses quiet.
                }
            }
            var done = Interlocked.Increment(ref completed);
            if (done % 256 == 0 || done == subnet.HostCount)
                log($"Subnet scan: {done:N0}/{subnet.HostCount:N0} addresses checked; {Volatile.Read(ref found)} Sonos device(s) found.");
        });
    }

    private async Task LoadSpeakersAsync(IEnumerable<Uri> locations, ConcurrentDictionary<string, Speaker> speakers, CancellationToken ct)
    {
        await Parallel.ForEachAsync(locations, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (location, token) =>
        {
            try
            {
                var speaker = await client.GetSpeakerAsync(location, token);
                speakers[speaker.Id] = speaker;
            }
            catch (Exception ex) when (IsConnectionError(ex, token))
            {
                log($"Could not connect to {location.Host}: {ex.Message}");
            }
        });
    }

    private async Task<IReadOnlyList<Uri>> DiscoverLocationsAsync(CancellationToken ct)
    {
        var addresses = settings.DiscoveryInterfaceAddress is not null
            ? [IPAddress.Parse(settings.DiscoveryInterfaceAddress)]
            : NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address).Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .Distinct().ToArray();
        if (addresses.Length == 0)
            log("No active IPv4 network interfaces. Set Sonos:SpeakerAddresses to a speaker IP.");
        var results = await Task.WhenAll(addresses.Select(address => DiscoverOnInterfaceAsync(address, ct)));
        return results.SelectMany(r => r).Distinct().ToArray();
    }

    private async Task<IReadOnlyList<Uri>> DiscoverOnInterfaceAsync(IPAddress address, CancellationToken ct)
    {
        var locations = new HashSet<Uri>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.DiscoveryTimeoutSeconds));
        try
        {
            using var udp = new UdpClient(new IPEndPoint(address, 0));
            udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
            udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 4);
            var message = Encoding.ASCII.GetBytes("M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 1\r\nST: urn:schemas-upnp-org:device:ZonePlayer:1\r\n\r\n");
            var destination = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
            for (var attempt = 0; attempt < 3; attempt++)
                await udp.SendAsync(message, destination, timeout.Token);
            while (true)
            {
                var response = await udp.ReceiveAsync(timeout.Token);
                foreach (var line in Encoding.UTF8.GetString(response.Buffer).Split("\r\n"))
                {
                    var colon = line.IndexOf(':');
                    if (colon > 0 && line[..colon].Equals("LOCATION", StringComparison.OrdinalIgnoreCase) &&
                        Uri.TryCreate(line[(colon + 1)..].Trim(), UriKind.Absolute, out var uri) && uri.Scheme == "http")
                        locations.Add(uri);
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        catch (SocketException ex) { log($"Discovery on {address}: {ex.Message}"); }
        ct.ThrowIfCancellationRequested();
        return locations.ToArray();
    }

    public static bool IsConnectionError(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or SonosException or XmlException or UriFormatException ||
        ex is OperationCanceledException && !ct.IsCancellationRequested;
}
