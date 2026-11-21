using System.Xml.Linq;

namespace AppServices.Sonos;

public sealed record SonosEndpoint(string Type, Uri ControlUri);
public sealed record Speaker(string Id, string Name, Uri DescriptionUri, IReadOnlyDictionary<string, SonosEndpoint> Services);
public sealed record GroupMember(string Id, string Name, Uri DescriptionUri, bool Visible);
public sealed record SpeakerGroup(string CoordinatorId, IReadOnlyList<GroupMember> Members);
public sealed record PlaybackTarget(Speaker Coordinator, IReadOnlyList<string> Rooms);
public sealed record SpeakerStatus(string State, string Title, string Artist, string Album,
    string Position, string Duration, string TrackUri, string Volume, bool Muted);
public sealed record LibraryPage(IReadOnlyList<LibraryItem> Items, int NumberReturned, int TotalMatches);

public sealed record LibraryItem(string Id, string Title, string Artist, string Album, bool IsContainer,
    string ResourceUri, string Metadata)
{
    public static LibraryItem FromXml(XElement element)
    {
        XNamespace didl = "urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/";
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        XNamespace upnp = "urn:schemas-upnp-org:metadata-1-0/upnp/";
        // Preserve the complete item's metadata, including its resource and Sonos-specific fields.
        var metadata = new XElement(didl + "DIDL-Lite",
            new XAttribute(XNamespace.Xmlns + "dc", dc),
            new XAttribute(XNamespace.Xmlns + "upnp", upnp),
            new XAttribute(XNamespace.Xmlns + "r", "urn:schemas-rinconnetworks-com:metadata-1-0/"),
            new XElement(element));
        return new LibraryItem((string?)element.Attribute("id") ?? "",
            (string?)element.Element(dc + "title") ?? "(untitled)",
            (string?)element.Element(upnp + "artist") ?? (string?)element.Element(dc + "creator") ?? "",
            (string?)element.Element(upnp + "album") ?? "",
            element.Name.LocalName == "container", (string?)element.Element(didl + "res") ?? "",
            metadata.ToString(SaveOptions.DisableFormatting));
    }
}
