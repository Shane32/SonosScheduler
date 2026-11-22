using System.Xml.Linq;

namespace SonosController;

public sealed record PlaybackTarget(Speaker Coordinator, IReadOnlyList<string> Rooms);
