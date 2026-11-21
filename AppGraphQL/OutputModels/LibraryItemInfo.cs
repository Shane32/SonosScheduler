namespace AppGraphQL.OutputModels;

[MapAutoClrType]
public sealed record LibraryItemInfo(string Id, string Title, string Artist, string Album, bool IsContainer, string ResourceUri, string Metadata);
