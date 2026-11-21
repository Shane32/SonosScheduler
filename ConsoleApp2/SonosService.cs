using System.Xml.Linq;

namespace SonosController;

public sealed record SonosService(string Type, Uri ControlUri);
