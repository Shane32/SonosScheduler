using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace SonosController;

public sealed class SonosClient(HttpClient http)
{
    private const string TRANSPORT = "AVTransport";
    private const string DIRECTORY = "ContentDirectory";
    private static readonly XNamespace _soap = "http://schemas.xmlsoap.org/soap/envelope/";

    public async Task<Speaker> GetSpeakerAsync(Uri descriptionUri, CancellationToken ct)
    {
        var document = XDocument.Parse(await http.GetStringAsync(descriptionUri, ct));
        var device = document.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "device")
            ?? throw new SonosException($"No device description at {descriptionUri}.");
        var id = Value(device, "UDN");
        if (id.StartsWith("uuid:", StringComparison.OrdinalIgnoreCase))
            id = id[5..];
        if (string.IsNullOrEmpty(id))
            throw new SonosException($"Missing speaker ID at {descriptionUri}.");
        var baseUrl = Value(document.Root!, "URLBase");
        var baseUri = string.IsNullOrWhiteSpace(baseUrl) ? descriptionUri : new Uri(baseUrl);
        var services = new Dictionary<string, SonosService>(StringComparer.Ordinal);
        foreach (var service in device.Descendants().Where(e => e.Name.LocalName == "service")) {
            var type = Value(service, "serviceType");
            var controlUrl = Value(service, "controlURL");
            var parts = type.Split(':');
            if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(controlUrl))
                services[parts[^2]] = new SonosService(type, new Uri(baseUri, controlUrl));
        }
        var name = Value(device, "roomName");
        if (string.IsNullOrEmpty(name))
            name = Value(device, "friendlyName");
        return new Speaker(id, string.IsNullOrEmpty(name) ? id : name, descriptionUri, services);
    }

    public async Task<IReadOnlyList<SpeakerGroup>> GetGroupsAsync(Speaker speaker, CancellationToken ct)
    {
        var response = await InvokeAsync(speaker, "ZoneGroupTopology", "GetZoneGroupState", ct);
        var state = XDocument.Parse(Value(response, "ZoneGroupState"));
        return state.Descendants().Where(e => e.Name.LocalName == "ZoneGroup").Select(group =>
            new SpeakerGroup((string?)group.Attribute("Coordinator") ?? "",
                group.Descendants().Where(e => e.Name.LocalName is "ZoneGroupMember" or "Satellite").Select(member =>
                    new GroupMember((string?)member.Attribute("UUID") ?? "",
                        (string?)member.Attribute("ZoneName") ?? "(unnamed)",
                        new Uri((string?)member.Attribute("Location") ?? throw new SonosException("Missing group member location.")),
                        member.Name.LocalName != "Satellite" && (string?)member.Attribute("Invisible") != "1" &&
                        (string?)member.Attribute("IsZoneBridge") != "1"))
                    .ToArray())).ToArray();
    }

    // Resolve on each operation so changes made in the Sonos app are picked up.
    public async Task<PlaybackTarget> ResolveTargetAsync(Speaker selected, CancellationToken ct)
    {
        var groups = await GetGroupsAsync(selected, ct);
        var group = groups.FirstOrDefault(g => g.Members.Any(m => m.Id == selected.Id))
            ?? throw new SonosException("The selected speaker is no longer in the topology. Refresh the speaker list.");
        var coordinator = group.Members.FirstOrDefault(m => m.Id == group.CoordinatorId)
            ?? throw new SonosException("The group's coordinator is unavailable. Refresh the speaker list.");
        var target = coordinator.Id == selected.Id ? selected : await GetSpeakerAsync(coordinator.DescriptionUri, ct);
        return new PlaybackTarget(target, group.Members.Where(m => m.Visible).Select(m => m.Name).ToArray());
    }

    public async Task<SpeakerStatus> GetStatusAsync(Speaker selected, Speaker coordinator, CancellationToken ct)
    {
        var transport = await InvokeAsync(coordinator, TRANSPORT, "GetTransportInfo", ct, ("InstanceID", "0"));
        var position = await InvokeAsync(coordinator, TRANSPORT, "GetPositionInfo", ct, ("InstanceID", "0"));
        var volume = await InvokeAsync(selected, "RenderingControl", "GetVolume", ct, ("InstanceID", "0"), ("Channel", "Master"));
        var mute = await InvokeAsync(selected, "RenderingControl", "GetMute", ct, ("InstanceID", "0"), ("Channel", "Master"));
        var metadata = Value(position, "TrackMetaData");
        LibraryItem? item = null;
        if (!string.IsNullOrWhiteSpace(metadata) && metadata != "NOT_IMPLEMENTED") {
            // Some sources report non-DIDL metadata; status should still show transport and URI.
            try {
                var element = XDocument.Parse(metadata).Root?.Elements().FirstOrDefault();
                if (element is not null)
                    item = LibraryItem.FromXml(element);
            } catch (XmlException) { }
        }
        return new SpeakerStatus(Value(transport, "CurrentTransportState"), item?.Title ?? "(no track metadata)",
            item?.Artist ?? "", item?.Album ?? "", Value(position, "RelTime"), Value(position, "TrackDuration"),
            Value(position, "TrackURI"), Value(volume, "CurrentVolume"), Value(mute, "CurrentMute") is "1" or "true");
    }

    public async Task<LibraryPage> BrowseAsync(Speaker speaker, string objectId, int start, int count, CancellationToken ct)
    {
        XElement response;
        try {
            response = await InvokeAsync(speaker, DIRECTORY, "Browse", ct,
                ("ObjectID", objectId), ("BrowseFlag", "BrowseDirectChildren"), ("Filter", "*"),
                ("StartingIndex", start.ToString(CultureInfo.InvariantCulture)),
                ("RequestedCount", count.ToString(CultureInfo.InvariantCulture)), ("SortCriteria", ""));
        } catch (SonosException ex) when (ex.ErrorCode == "701") {
            // Sonos uses "No such object" for a search with no matches as well.
            return new LibraryPage([], 0, 0);
        }
        var result = Value(response, "Result");
        var items = string.IsNullOrWhiteSpace(result) ? [] :
            XDocument.Parse(result).Root!.Elements().Select(LibraryItem.FromXml).ToArray();
        return new LibraryPage(items, Number(response, "NumberReturned"), Number(response, "TotalMatches"));
    }

    public async Task PlayLibraryItemAsync(Speaker coordinator, LibraryItem item, CancellationToken ct)
    {
        if (item.IsContainer || string.IsNullOrWhiteSpace(item.ResourceUri))
            throw new SonosException("Select a song with a playable resource URI.");
        // Append rather than clearing the existing queue, then jump to the returned 1-based position.
        var added = await InvokeAsync(coordinator, TRANSPORT, "AddURIToQueue", ct,
            ("InstanceID", "0"), ("EnqueuedURI", item.ResourceUri), ("EnqueuedURIMetaData", item.Metadata),
            ("DesiredFirstTrackNumberEnqueued", "0"), ("EnqueueAsNext", "0"));
        var track = Number(added, "FirstTrackNumberEnqueued");
        if (track < 1)
            throw new SonosException("Sonos did not return a valid queue position.");
        await InvokeAsync(coordinator, TRANSPORT, "SetAVTransportURI", ct, ("InstanceID", "0"),
            ("CurrentURI", $"x-rincon-queue:{coordinator.Id}#0"), ("CurrentURIMetaData", ""));
        await InvokeAsync(coordinator, TRANSPORT, "Seek", ct, ("InstanceID", "0"), ("Unit", "TRACK_NR"),
            ("Target", track.ToString(CultureInfo.InvariantCulture)));
        await ResumeAsync(coordinator, ct);
    }

    public Task ResumeAsync(Speaker coordinator, CancellationToken ct) =>
        InvokeAsync(coordinator, TRANSPORT, "Play", ct, ("InstanceID", "0"), ("Speed", "1"));

    public Task PauseAsync(Speaker coordinator, CancellationToken ct) =>
        InvokeAsync(coordinator, TRANSPORT, "Pause", ct, ("InstanceID", "0"));

    private async Task<XElement> InvokeAsync(Speaker speaker, string serviceName, string action, CancellationToken ct,
        params (string Name, string Value)[] arguments)
    {
        if (!speaker.Services.TryGetValue(serviceName, out var service))
            throw new SonosException($"{speaker.Name} does not support {serviceName}.");
        XNamespace ns = service.Type;
        var envelope = new XElement(_soap + "Envelope", new XAttribute(XNamespace.Xmlns + "s", _soap),
            new XAttribute(_soap + "encodingStyle", "http://schemas.xmlsoap.org/soap/encoding/"),
            new XElement(_soap + "Body", new XElement(ns + action,
                new XAttribute(XNamespace.Xmlns + "u", ns),
                arguments.Select(a => new XElement(a.Name, a.Value)))));
        using var request = new HttpRequestMessage(HttpMethod.Post, service.ControlUri);
        request.Headers.Add("SOAPACTION", $"\"{service.Type}#{action}\"");
        request.Content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml");
        using var response = await http.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        XDocument document;
        try { document = XDocument.Parse(content); } catch (XmlException ex) {
            throw new SonosException($"{speaker.Name}: {action} returned HTTP {(int)response.StatusCode} with invalid XML: {ex.Message}");
        }
        var fault = document.Descendants(_soap + "Fault").FirstOrDefault();
        if (fault is not null) {
            var code = Value(fault, "errorCode");
            var description = Value(fault, "errorDescription");
            if (string.IsNullOrEmpty(description))
                description = Value(fault, "faultstring");
            throw new SonosException($"{speaker.Name}: {action} failed ({code}): {description}", code);
        }
        response.EnsureSuccessStatusCode();
        return document.Root?.Element(_soap + "Body")?.Elements().FirstOrDefault()
            ?? throw new SonosException($"{speaker.Name}: {action} returned an empty SOAP response.");
    }

    private static string Value(XElement element, string name) =>
        element.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value ?? "";

    private static int Number(XElement element, string name) =>
        int.TryParse(Value(element, name), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value :
            throw new SonosException($"Sonos returned an invalid {name}.");
}
