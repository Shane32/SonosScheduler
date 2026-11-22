using System.Xml.Linq;

namespace AppServices.Sonos;

public sealed record SonosEndpoint(string Type, Uri ControlUri);
