using System.Xml.Linq;

namespace SonosController;

public sealed record SpeakerStatus(string State, string Title, string Artist, string Album,
    string Position, string Duration, string TrackUri, string Volume, bool Muted);
