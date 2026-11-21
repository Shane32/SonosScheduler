namespace SonosController;

public sealed class ConsoleMenu(SonosClient client, SonosDiscovery discovery, SonosSettings settings)
{
    public async Task RunAsync(CancellationToken ct)
    {
        Console.WriteLine("Sonos local controller\nCtrl+C to exit");
        while (!ct.IsCancellationRequested) {
            Console.WriteLine("\nFinding speakers...");
            var speakers = await discovery.FindSpeakersAsync(ct);
            if (speakers.Count == 0) {
                Console.WriteLine("No speakers found. Check network access to Sonos on TCP 1400 (and UDP 1900 for automatic discovery).\nSet Sonos:SpeakerAddresses to a speaker IP or Sonos:DiscoverySubnet to an IPv4 CIDR range in appsettings.json.");
            }
            for (var i = 0; i < speakers.Count; i++)
                Console.WriteLine($"{i + 1}. {speakers[i].Name} ({speakers[i].DescriptionUri.Host})");
            var input = Read("Select a speaker number, R to refresh, or Q to quit: ", ct);
            if (input is null or "q")
                return;
            if (input == "r")
                continue;
            if (int.TryParse(input, out var number) && number >= 1 && number <= speakers.Count) {
                await SpeakerMenuAsync(speakers[number - 1], ct);
                if (QuitRequested)
                    return;
            } else
                Console.WriteLine("Enter one of the listed speaker numbers.");
        }
    }

    private async Task SpeakerMenuAsync(Speaker speaker, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested) {
            Console.WriteLine($"\nSelected: {speaker.Name}\n1. View status\n2. Browse music library\n3. Search library by song title\n4. Browse shared folders\n5. Pause\n6. Resume\nB. Choose another speaker\nQ. Quit");
            var input = Read("> ", ct);
            if (input is null or "q") { QuitRequested = true; return; }
            if (input == "b")
                return;
            if (input is not ("1" or "2" or "3" or "4" or "5" or "6"))
                continue;
            try {
                var target = await client.ResolveTargetAsync(speaker, ct);
                Console.WriteLine($"Group: {string.Join(", ", target.Rooms)} | Coordinator: {target.Coordinator.Name}");
                if (input == "1") {
                    var status = await client.GetStatusAsync(speaker, target.Coordinator, ct);
                    Console.WriteLine($"State: {status.State}\nSong: {status.Title}\nArtist: {status.Artist}\nAlbum: {status.Album}\nPosition: {status.Position} / {status.Duration}\nVolume ({speaker.Name}): {status.Volume}% | Muted: {status.Muted}\nURI: {status.TrackUri}");
                } else if (input is "2" or "3" or "4") {
                    var objectId = input == "4" ? "S:" : "A:";
                    var title = input == "4" ? "Shared folders" : "Music library";
                    if (input == "3") {
                        var term = Read("Song title (blank to go back): ", ct, normalize: false);
                        if (string.IsNullOrWhiteSpace(term))
                            continue;
                        objectId = "A:TRACKS:" + Uri.EscapeDataString(term);
                        title = $"Search: {term}";
                    }
                    await BrowseAsync(speaker, objectId, title, ct);
                    if (QuitRequested)
                        return;
                } else if (input == "5") {
                    await client.PauseAsync(target.Coordinator, ct);
                    Console.WriteLine("Group paused.");
                } else {
                    await client.ResumeAsync(target.Coordinator, ct);
                    Console.WriteLine("Group resumed.");
                }
            } catch (Exception ex) when (SonosDiscovery.IsConnectionError(ex, ct)) {
                ShowError(ex);
            }
        }
    }

    private bool QuitRequested { get; set; }

    private async Task BrowseAsync(Speaker selected, string objectId, string title, CancellationToken ct)
    {
        var history = new Stack<(string Id, string Title, int Start)>();
        var previousPages = new Stack<int>();
        // Remember page boundaries for each container; Sonos can return fewer than requested.
        var pageHistory = new Stack<int[]>();
        var start = 0;
        while (!ct.IsCancellationRequested) {
            var target = await client.ResolveTargetAsync(selected, ct);
            var page = await client.BrowseAsync(target.Coordinator, objectId, start, settings.LibraryPageSize, ct);
            Console.WriteLine($"\n{title} — {page.TotalMatches} result(s)");
            for (var i = 0; i < page.Items.Count; i++) {
                var item = page.Items[i];
                var details = string.Join(" — ", new[] { item.Artist, item.Album }.Where(s => !string.IsNullOrWhiteSpace(s)));
                Console.WriteLine($"{i + 1}. {(item.IsContainer ? "[Folder] " : "")}{item.Title}{(details.Length > 0 ? " — " + details : "")}");
            }
            if (page.Items.Count == 0)
                Console.WriteLine("No songs found here. Make sure the network share is configured and indexed in the Sonos app.");
            else
                Console.WriteLine($"Showing {start + 1}–{start + page.Items.Count} of {page.TotalMatches}.");
            Console.WriteLine($"Selecting a song starts playback in: {string.Join(", ", target.Rooms)}.");
            var input = Read("Number to open/play | N next | P previous | B back | Q quit: ", ct);
            if (input is null or "q") { QuitRequested = true; return; }
            if (input == "b") {
                if (history.Count == 0)
                    return;
                (objectId, title, start) = history.Pop();
                previousPages = new Stack<int>(pageHistory.Pop().Reverse());
            } else if (input == "n") {
                if (page.NumberReturned > 0 && start + page.NumberReturned < page.TotalMatches) {
                    previousPages.Push(start);
                    start += page.NumberReturned;
                } else
                    Console.WriteLine("No next page.");
            } else if (input == "p") {
                if (previousPages.TryPop(out var previous))
                    start = previous;
                else
                    Console.WriteLine("Already on the first page.");
            } else if (int.TryParse(input, out var number) && number >= 1 && number <= page.Items.Count) {
                var item = page.Items[number - 1];
                if (item.IsContainer) {
                    history.Push((objectId, title, start));
                    pageHistory.Push(previousPages.ToArray());
                    previousPages.Clear();
                    objectId = item.Id;
                    title = item.Title;
                    start = 0;
                } else {
                    // Recheck grouping immediately before changing playback.
                    var current = await client.ResolveTargetAsync(selected, ct);
                    await client.PlayLibraryItemAsync(current.Coordinator, item, ct);
                    Console.WriteLine($"Playing '{item.Title}' in {string.Join(", ", current.Rooms)}. Existing queue retained.");
                    return;
                }
            }
        }
    }

    private static string? Read(string prompt, CancellationToken ct, bool normalize = true)
    {
        ct.ThrowIfCancellationRequested();
        Console.Write(prompt);
        // Console's synchronized reader can block even through ReadLineAsync. Wait on a
        // background read so Ctrl+C can exit while the terminal is waiting for a line.
        var input = Task.Run(Console.ReadLine, CancellationToken.None).WaitAsync(ct).GetAwaiter().GetResult()?.Trim();
        ct.ThrowIfCancellationRequested();
        return normalize ? input?.ToLowerInvariant() : input;
    }

    private static void ShowError(Exception ex)
    {
        Console.WriteLine(ex is OperationCanceledException ? "Request timed out. Check the speaker address and network connection." : $"Error: {ex.Message}");
    }
}
