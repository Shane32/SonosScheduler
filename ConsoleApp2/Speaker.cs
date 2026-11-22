using System.Xml.Linq;

namespace SonosController;

public sealed record Speaker(string Id, string Name, Uri DescriptionUri, IReadOnlyDictionary<string, SonosService> Services);
