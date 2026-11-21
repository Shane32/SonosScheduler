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

public sealed class SonosSettings
{
    public string[] SpeakerAddresses { get; init; } = [];
    public string? DiscoverySubnet { get; init; }
    public int SubnetScanTimeoutSeconds { get; init; } = 2;
    public int SubnetScanConcurrency { get; init; } = 64;
    public int DiscoveryTimeoutSeconds { get; init; } = 5;
    public string? DiscoveryInterfaceAddress { get; init; }
    public int HttpTimeoutSeconds { get; init; } = 10;
    public int LibraryPageSize { get; init; } = 25;

    public void Validate()
    {
        if (DiscoveryTimeoutSeconds is < 1 or > 60 || HttpTimeoutSeconds is < 1 or > 120)
            throw new InvalidDataException("DiscoveryTimeoutSeconds must be 1–60; HttpTimeoutSeconds must be 1–120.");
        if (LibraryPageSize is < 1 or > 200)
            throw new InvalidDataException("LibraryPageSize must be 1–200.");
        if (SpeakerAddresses is null)
            throw new InvalidDataException("SpeakerAddresses must be an array (use [] for automatic discovery).");
        foreach (var address in SpeakerAddresses)
            _ = DescriptionUri(address);
        if (!string.IsNullOrWhiteSpace(DiscoverySubnet))
            _ = IPv4Subnet.Parse(DiscoverySubnet);
        if (SubnetScanTimeoutSeconds is < 1 or > 30)
            throw new InvalidDataException("SubnetScanTimeoutSeconds must be 1–30.");
        if (SubnetScanConcurrency is < 1 or > 256)
            throw new InvalidDataException("SubnetScanConcurrency must be 1–256.");
        if (DiscoveryInterfaceAddress is not null &&
            (!IPAddress.TryParse(DiscoveryInterfaceAddress, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork))
            throw new InvalidDataException("DiscoveryInterfaceAddress must be a local IPv4 address or null.");
    }

    public static Uri DescriptionUri(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new InvalidDataException("SpeakerAddresses cannot contain blank addresses.");
        var value = address.Contains("://", StringComparison.Ordinal) ? address : $"http://{address}";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "http" ||
            string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidDataException($"Invalid Sonos address: {address}. Use an IP address, hostname, or HTTP URL.");
        var builder = new UriBuilder(uri);
        // Sonos's local HTTP service uses port 1400. Explicit non-default ports are supported too.
        if (uri.IsDefaultPort)
            builder.Port = 1400;
        if (uri.AbsolutePath == "/")
            builder.Path = "/xml/device_description.xml";
        return builder.Uri;
    }
}
