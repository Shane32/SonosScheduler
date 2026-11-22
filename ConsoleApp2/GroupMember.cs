using System.Xml.Linq;

namespace SonosController;

public sealed record GroupMember(string Id, string Name, Uri DescriptionUri, bool Visible);
