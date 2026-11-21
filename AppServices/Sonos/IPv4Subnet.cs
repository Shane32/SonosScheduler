using System.Globalization;
using System.Net;

namespace AppServices.Sonos;

public sealed class IPv4Subnet
{
    private readonly uint _network;
    private readonly int _prefixLength;

    private IPv4Subnet(uint network, int prefixLength)
    {
        _network = network;
        _prefixLength = prefixLength;
    }

    public int HostCount => _prefixLength >= 31 ? 1 << (32 - _prefixLength) : (1 << (32 - _prefixLength)) - 2;

    public static IPv4Subnet Parse(string cidr)
    {
        var parts = cidr.Trim().Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var prefix) ||
            prefix is < 16 or > 32)
            throw InvalidSubnet();
        var octets = parts[0].Split('.');
        if (octets.Length != 4 || octets.Any(o => !byte.TryParse(o, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            throw InvalidSubnet();
        var bytes = octets.Select(o => byte.Parse(o, CultureInfo.InvariantCulture)).ToArray();
        var address = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        var mask = uint.MaxValue << (32 - prefix);
        return new IPv4Subnet(address & mask, prefix);
    }

    public IEnumerable<IPAddress> GetHostAddresses()
    {
        // /31 point-to-point ranges and /32 single-host ranges have no reserved endpoints.
        var first = _prefixLength >= 31 ? _network : _network + 1;
        for (var i = 0; i < HostCount; i++)
            yield return Address(first + (uint)i);
    }

    public bool Contains(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
            return false;
        var value = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        return (value & (uint.MaxValue << (32 - _prefixLength))) == _network;
    }

    public override string ToString() => $"{Address(_network)}/{_prefixLength}";

    private static IPAddress Address(uint value) =>
        new(new byte[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value });

    private static InvalidDataException InvalidSubnet() => new(
        "DiscoverySubnet must be IPv4 CIDR with a prefix from /16 through /32, for example 192.168.50.0/24. Use null for default discovery.");
}
