namespace AppGraphQL.OutputModels;

[MapAutoClrType]
public sealed record SessionInfo(bool IsAdmin, string? Name);
