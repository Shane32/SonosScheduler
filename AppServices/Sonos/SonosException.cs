using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace AppServices.Sonos;

public sealed class SonosException(string message, string? errorCode = null) : Exception(message)
{
    public string? ErrorCode { get; } = errorCode;
}
