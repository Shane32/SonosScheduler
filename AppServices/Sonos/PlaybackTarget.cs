using System.Xml.Linq;

namespace AppServices.Sonos;

public sealed record PlaybackTarget(Speaker Coordinator, IReadOnlyList<string> Rooms);
