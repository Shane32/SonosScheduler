using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace SonosController;

public sealed class AppSettings
{
    private static readonly JsonSerializerOptions _serializerOptions = new() {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public SonosSettings Sonos { get; init; } = new();

    public static SonosSettings Load(string path)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), _serializerOptions)
            ?? throw new InvalidDataException("The settings file is empty.");
        if (settings.Sonos is null)
            throw new InvalidDataException("The Sonos settings section cannot be null.");
        settings.Sonos.Validate();
        return settings.Sonos;
    }
}
