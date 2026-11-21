using System.Net;
using System.Text;
using System.Xml.Linq;

namespace Services;

internal sealed class FakeSonosMessageHandler : HttpMessageHandler
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
        string Arg(string name) =>
            body.Descendants().First(x => x.Name.LocalName == name).Value;
        var values = new List<XElement>();
        switch (action) {
            case "GetZoneGroupState":
                XElement Member(string name, string ip) =>
                    new("ZoneGroupMember", new XAttribute("UUID", "RINCON_" + name.ToUpperInvariant()), new XAttribute("ZoneName", name), new XAttribute("Location", $"http://{ip}:1400/xml/device_description.xml"));
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
    private static HttpResponseMessage Response(string text) =>
        new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "text/xml") };
}
