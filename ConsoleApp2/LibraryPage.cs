using System.Xml.Linq;

namespace SonosController;

public sealed record LibraryPage(IReadOnlyList<LibraryItem> Items, int NumberReturned, int TotalMatches);
