using System.Xml.Linq;

namespace AppServices.Sonos;

public sealed record Speaker(string Id, string Name, Uri DescriptionUri, IReadOnlyDictionary<string, SonosEndpoint> Services);
