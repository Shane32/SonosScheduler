using AppServices.Sonos;

namespace AppGraphQL.OutputModels;

[MapAutoClrType]
public sealed record SessionInfo(bool IsAdmin, string? Name);

[MapAutoClrType]
public sealed record LibraryItemInfo(string Id, string Title, string Artist, string Album, bool IsContainer, string ResourceUri, string Metadata);

[MapAutoClrType]
public sealed record LibraryPageInfo(IReadOnlyList<LibraryItemInfo> Items, int NumberReturned, int TotalMatches)
{
    [Ignore]
    public static LibraryPageInfo From(LibraryPage page) => new(page.Items.Select(x => new LibraryItemInfo(x.Id, x.Title, x.Artist, x.Album, x.IsContainer, x.ResourceUri, x.Metadata)).ToArray(), page.NumberReturned, page.TotalMatches);
}
