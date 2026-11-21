using AppGraphQL.MutationGraphs;

namespace AppGraphQL;

public sealed class Mutation : DIObjectGraphBase
{
    [DIGraph(typeof(GroupMutation))] public static string Groups() => string.Empty;
    [DIGraph(typeof(SpeakerMutation))] public static string Speakers() => string.Empty;
    [DIGraph(typeof(ScheduleMutation))] public static string Schedules() => string.Empty;
    [DIGraph(typeof(AdminMutation))] public static string Admins() => string.Empty;
}
