using System.Net;
using System.Text;
using System.Xml.Linq;
using SonosController;

internal sealed record RecordedRequest(string Method, string Host, string Action, Dictionary<string, string> Arguments);
