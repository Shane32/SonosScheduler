using System.Xml.Linq;

namespace AppServices.Sonos;

public sealed record LibraryPage(IReadOnlyList<LibraryItem> Items, int NumberReturned, int TotalMatches);
