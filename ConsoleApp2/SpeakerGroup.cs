using System.Xml.Linq;

namespace SonosController;

public sealed record SpeakerGroup(string CoordinatorId, IReadOnlyList<GroupMember> Members);
