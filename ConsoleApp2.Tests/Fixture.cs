using System.Net;
using System.Text;
using System.Xml.Linq;
using SonosController;

internal sealed class Fixture : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = [];
    public Func<RecordedRequest, HttpResponseMessage?>? Override { get; set; }
    public Func<RecordedRequest, CancellationToken, Task<HttpResponseMessage?>>? OverrideAsync { get; set; }
    private readonly HttpClient _http;
    public SonosClient Client { get; }
    public Fixture() { _http = new HttpClient(this); Client = new SonosClient(_http); }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Method == HttpMethod.Get) {
            var get = new RecordedRequest("GET", request.RequestUri!.Host, "", []);
            lock (Requests)
                Requests.Add(get);
            if (OverrideAsync is not null && await OverrideAsync(get, cancellationToken) is { } asyncResult)
                return asyncResult;
            if (Override?.Invoke(get) is { } result)
                return result;
            return Xml(Description(request.RequestUri.Host));
        }
        var envelope = XDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        var action = envelope.Root!.Elements().Single().Elements().Single();
        var recorded = new RecordedRequest("POST", request.RequestUri!.Host, action.Name.LocalName,
            action.Elements().ToDictionary(e => e.Name.LocalName, e => e.Value));
        lock (Requests)
            Requests.Add(recorded);
        var header = request.Headers.GetValues("SOAPACTION").Single();
        if (header != $"\"{action.Name.NamespaceName}#{action.Name.LocalName}\"")
            throw new InvalidOperationException("Incorrect SOAPAction.");
        if (request.Content.Headers.ContentType?.MediaType != "text/xml")
            throw new InvalidOperationException("Incorrect content type.");
        if (request.RequestUri.AbsolutePath != $"/control/{action.Name.NamespaceName.Split(':')[^2]}")
            throw new InvalidOperationException("Did not use advertised service URL.");
        if (Override?.Invoke(recorded) is { } overridden)
            return overridden;
        var fields = recorded.Action switch {
            "GetZoneGroupState" => new[] { Field("ZoneGroupState", TOPOLOGY) },
            "Browse" => [Field("Result", Didl), Field("NumberReturned", "1"), Field("TotalMatches", "30")],
            "GetTransportInfo" => [Field("CurrentTransportState", "PLAYING")],
            "GetPositionInfo" => [Field("TrackMetaData", Didl), Field("TrackURI", "x-file-cifs://nas/Music/song.flac"), Field("RelTime", "0:01:02"), Field("TrackDuration", "0:03:45")],
            "GetVolume" => [Field("CurrentVolume", "28")],
            "GetMute" => [Field("CurrentMute", "1")],
            "AddURIToQueue" => [Field("FirstTrackNumberEnqueued", "7")],
            _ => Array.Empty<XElement>()
        };
        return Xml(Envelope(new XElement(action.Name.Namespace + (recorded.Action + "Response"), fields)));
    }

    private static XElement Field(string name, string value) => new(name, value);
    private static HttpResponseMessage Xml(string content, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(content, Encoding.UTF8, "text/xml") };
    private static string Envelope(XElement body)
    {
        XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
        return new XElement(soap + "Envelope", new XElement(soap + "Body", body)).ToString();
    }
    public static HttpResponseMessage Fault(string code, string description)
    {
        XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
        return Xml(Envelope(new XElement(soap + "Fault", Field("faultstring", "UPnPError"),
            new XElement("detail", new XElement("UPnPError", Field("errorCode", code), Field("errorDescription", description))))), HttpStatusCode.InternalServerError);
    }
    public static HttpResponseMessage LibraryResponse(string didl, int returned, int total) => Xml(Envelope(
        new XElement(XName.Get("BrowseResponse", "urn:schemas-upnp-org:service:ContentDirectory:1"),
            Field("Result", didl), Field("NumberReturned", returned.ToString(CultureInfo.InvariantCulture)), Field("TotalMatches", total.ToString(CultureInfo.InvariantCulture)))));
    public static HttpResponseMessage StandaloneTopologyResponse(string host) => Xml(Envelope(
        new XElement(XName.Get("GetZoneGroupStateResponse", "urn:schemas-upnp-org:service:ZoneGroupTopology:1"),
            Field("ZoneGroupState", new XElement("ZoneGroupState", new XElement("ZoneGroups", new XElement("ZoneGroup",
                new XAttribute("Coordinator", $"RINCON_{host.ToUpperInvariant()}"), new XElement("ZoneGroupMember",
                    new XAttribute("UUID", $"RINCON_{host.ToUpperInvariant()}"), new XAttribute("ZoneName", "Remote speaker"),
                    new XAttribute("Location", $"http://{host}:1400/xml/device_description.xml"))))).ToString()))));
    private static string Description(string host)
    {
        XNamespace ns = "urn:schemas-upnp-org:device-1-0";
        var services = new[] { "AVTransport", "RenderingControl", "ContentDirectory", "ZoneGroupTopology" };
        return new XElement(ns + "root", new XElement(ns + "device",
            new XElement(ns + "UDN", $"uuid:RINCON_{host.ToUpperInvariant()}"),
            new XElement(ns + "roomName", host == "living" ? "Living room" : "Kitchen"),
            new XElement(ns + "serviceList", services.Select(s => new XElement(ns + "service",
                new XElement(ns + "serviceType", $"urn:schemas-upnp-org:service:{s}:1"),
                new XElement(ns + "controlURL", $"/control/{s}")))))).ToString();
    }
    private const string TOPOLOGY = """
        <ZoneGroupState><ZoneGroups><ZoneGroup Coordinator="RINCON_LIVING" ID="group:1">
          <ZoneGroupMember UUID="RINCON_LIVING" ZoneName="Living room" Location="http://living:1400/xml/device_description.xml">
            <Satellite UUID="RINCON_SATELLITE" ZoneName="Surround" Location="http://satellite:1400/xml/device_description.xml" />
          </ZoneGroupMember>
          <ZoneGroupMember UUID="RINCON_KITCHEN" ZoneName="Kitchen" Location="http://kitchen:1400/xml/device_description.xml" />
          <ZoneGroupMember UUID="RINCON_SUB" ZoneName="Sub" Invisible="1" Location="http://sub:1400/xml/device_description.xml" />
        </ZoneGroup></ZoneGroups></ZoneGroupState>
        """;
    public const string Didl = """
        <DIDL-Lite xmlns="urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:upnp="urn:schemas-upnp-org:metadata-1-0/upnp/" xmlns:r="urn:schemas-rinconnetworks-com:metadata-1-0/">
          <item id="S:track&amp;1" parentID="A:TRACKS" restricted="true">
            <dc:title>Rock &amp; Roll &lt;Live&gt;</dc:title><upnp:artist>A &amp; B</upnp:artist><upnp:album>The Album</upnp:album>
            <upnp:class>object.item.audioItem.musicTrack</upnp:class><r:custom>keep me</r:custom>
            <res protocolInfo="x-file-cifs:*:audio/flac:*">x-file-cifs://nas/Music/Rock%20%26%20Roll.flac</res>
          </item>
        </DIDL-Lite>
        """;
    // HttpClient owns this handler; ordinary using disposal is sufficient for this in-memory fixture.
}
