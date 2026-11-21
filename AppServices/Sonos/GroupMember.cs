using System.Xml.Linq;

namespace AppServices.Sonos;

public sealed record GroupMember(string Id, string Name, Uri DescriptionUri, bool Visible);
