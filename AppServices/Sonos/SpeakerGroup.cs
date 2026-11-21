using System.Xml.Linq;

namespace AppServices.Sonos;

public sealed record SpeakerGroup(string CoordinatorId, IReadOnlyList<GroupMember> Members);
