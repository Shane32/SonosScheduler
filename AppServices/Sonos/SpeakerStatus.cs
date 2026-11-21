using System.Xml.Linq;

namespace AppServices.Sonos;

public sealed record SpeakerStatus(string State, string Title, string Artist, string Album,
    string Position, string Duration, string TrackUri, string Volume, bool Muted);
