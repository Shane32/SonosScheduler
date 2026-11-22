using System.Collections.Concurrent;
using System.Net;
using System.Xml;
using DbSpeaker = AppDb.Models.Speaker;

namespace AppServices.Sonos;

public sealed class SonosService(AppDbContext db, SonosClient client, TimeProvider time, AuditService audit)
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

    public async Task RefreshAsync(int groupId, CancellationToken ct)
    {
        var gate = _locks.GetOrAdd(groupId, _ => new(1));
        await gate.WaitAsync(ct);
        try { await RefreshCoreAsync(groupId, ct); } finally { gate.Release(); }
    }

    private async Task RefreshCoreAsync(int groupId, CancellationToken ct)
    {
        var group = await db.Groups.Include(x => x.Speakers).SingleAsync(x => x.Id == groupId, ct);
        var subnet = IPv4Subnet.Parse(group.Subnet);
        var seeds = group.Speakers.OrderByDescending(x => x.IsOnline).Select(x => x.Address).ToList();
        if (group.SeedAddress is not null)
            seeds.Insert(0, group.SeedAddress);
        IReadOnlyList<SpeakerGroup>? topology = null;
        foreach (var address in seeds.Distinct()) {
            try {
                var seed = await LoadAsync(DescriptionUri(address), subnet, ct);
                topology = await client.GetGroupsAsync(seed, ct);
                break;
            } catch (Exception ex) when (ConnectionError(ex, ct)) { }
        }
        if (topology is null) {
            using var found = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try {
                await Parallel.ForEachAsync(subnet.GetHostAddresses(), new ParallelOptions { MaxDegreeOfParallelism = 24, CancellationToken = found.Token }, async (address, token) => {
                    try {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                        timeout.CancelAfter(TimeSpan.FromSeconds(1));
                        var seed = await LoadAsync(DescriptionUri(address.ToString()), subnet, timeout.Token);
                        var state = await client.GetGroupsAsync(seed, timeout.Token);
                        if (Interlocked.CompareExchange(ref topology, state, null) is null)
                            await found.CancelAsync();
                    } catch (Exception ex) when (ConnectionError(ex, ct) || ex is OperationCanceledException && found.IsCancellationRequested && !ct.IsCancellationRequested) { }
                });
            } catch (OperationCanceledException) when (!ct.IsCancellationRequested && topology is not null) { }
        }
        foreach (var speaker in group.Speakers)
            speaker.IsOnline = false;
        if (topology is null) {
            group.LastError = "No reachable Sonos speaker found in this subnet.";
            group.LastRefreshedAt = time.GetUtcNow();
            audit.Add("Discovery", $"{group.Name}: {group.LastError}", level: "Warning");
            await db.SaveChangesAsync(ct);
            return;
        }
        var errors = new List<string>();
        foreach (var liveGroup in topology) {
            foreach (var member in liveGroup.Members.Where(x => x.Visible)) {
                if (!ValidLocation(member.DescriptionUri, subnet))
                    continue;
                var row = group.Speakers.FirstOrDefault(x => x.SonosId == member.Id);
                if (row is null) {
                    row = new DbSpeaker { GroupId = groupId, SonosId = member.Id };
                    group.Speakers.Add(row);
                }
                row.Name = member.Name;
                row.Address = member.DescriptionUri.Host;
                row.DescriptionUrl = member.DescriptionUri.ToString();
                row.CoordinatorId = liveGroup.CoordinatorId;
                try {
                    var selected = await LoadAsync(member.DescriptionUri, subnet, ct);
                    var coordinatorMember = liveGroup.Members.First(x => x.Id == liveGroup.CoordinatorId);
                    var coordinator = selected.Id == liveGroup.CoordinatorId ? selected : await LoadAsync(coordinatorMember.DescriptionUri, subnet, ct);
                    ApplyStatus(row, await client.GetStatusAsync(selected, coordinator, ct));
                } catch (Exception ex) when (ConnectionError(ex, ct)) { errors.Add($"{row.Name}: {ex.Message}"); }
            }
        }
        group.LastRefreshedAt = time.GetUtcNow();
        group.LastError = errors.Count == 0 ? null : string.Join("; ", errors)[..Math.Min(2000, string.Join("; ", errors).Length)];
        audit.Add("Discovery", $"Refreshed {group.Name}: {group.Speakers.Count(x => x.IsOnline)} reachable rooms.");
        await db.SaveChangesAsync(ct);
    }

    public async Task RefreshStatusAsync(int speakerId, CancellationToken ct)
    {
        var row = await db.Speakers.Include(x => x.Group).SingleAsync(x => x.Id == speakerId, ct);
        try {
            var selected = await LoadAsync(new Uri(row.DescriptionUrl), IPv4Subnet.Parse(row.Group.Subnet), ct);
            var target = await ResolveTargetAsync(selected, row.Group, ct);
            row.CoordinatorId = target.Id;
            ApplyStatus(row, await client.GetStatusAsync(selected, target, ct));
            await db.SaveChangesAsync(ct);
        } catch (Exception ex) when (ConnectionError(ex, ct)) {
            row.IsOnline = false;
            await db.SaveChangesAsync(ct);
            throw new ClientError("Speaker is unavailable. Refresh the subnet to rediscover it.", ex);
        }
    }

    public async Task ControlAsync(int speakerId, string action, int? volume, CancellationToken ct)
    {
        var row = await db.Speakers.Include(x => x.Group).SingleAsync(x => x.Id == speakerId, ct);
        var gate = _locks.GetOrAdd(row.GroupId, _ => new(1));
        await gate.WaitAsync(ct);
        try {
            var selected = await LoadAsync(new Uri(row.DescriptionUrl), IPv4Subnet.Parse(row.Group.Subnet), ct);
            var target = await ResolveTargetAsync(selected, row.Group, ct);
            switch (action) {
                case "play":
                    await client.ResumeAsync(target, ct);
                    break;
                case "pause":
                    await client.PauseAsync(target, ct);
                    break;
                case "volume" when volume is >= 0 and <= 100:
                    await client.SetVolumeAsync(selected, volume.Value, ct);
                    break;
                default:
                    throw new ClientError("Choose play, pause, or a volume between 0 and 100.");
            }
            await RefreshStatusAsync(speakerId, ct);
        } finally { gate.Release(); }
    }

    public async Task SetMembersAsync(int coordinatorId, IReadOnlyList<int> speakerIds, CancellationToken ct)
    {
        var row = await db.Speakers.Include(x => x.Group).SingleAsync(x => x.Id == coordinatorId, ct);
        var ids = speakerIds.Append(coordinatorId).Distinct().ToArray();
        var rows = await db.Speakers.Where(x => ids.Contains(x.Id) && x.GroupId == row.GroupId).ToListAsync(ct);
        if (rows.Count != ids.Length)
            throw new ClientError("All rooms must belong to the same subnet group.");
        var gate = _locks.GetOrAdd(row.GroupId, _ => new(1));
        await gate.WaitAsync(ct);
        try {
            var subnet = IPv4Subnet.Parse(row.Group.Subnet);
            var coordinator = await LoadAsync(new Uri(row.DescriptionUrl), subnet, ct);
            var topology = await client.GetGroupsAsync(coordinator, ct);
            var current = topology.First(x => x.Members.Any(m => m.Id == coordinator.Id));
            foreach (var member in current.Members.Where(x => x.Visible && !rows.Any(r => r.SonosId == x.Id)))
                await client.StandaloneAsync(await LoadAsync(member.DescriptionUri, subnet, ct), ct);
            await ExtractAsync(coordinator, topology, rows.Select(x => x.SonosId).ToHashSet(), ct);
            foreach (var member in rows.Where(x => x.Id != coordinatorId)) {
                var selected = await LoadAsync(new Uri(member.DescriptionUrl), subnet, ct);
                await ExtractAsync(selected, await client.GetGroupsAsync(selected, ct), rows.Select(x => x.SonosId).ToHashSet(), ct);
                await client.JoinAsync(selected, coordinator, ct);
            }
            await RefreshCoreAsync(row.GroupId, ct);
        } finally { gate.Release(); }
    }

    public async Task<LibraryPage> BrowseAsync(int groupId, string objectId, int start, int count, CancellationToken ct)
    {
        ValidateObjectId(objectId);
        if (start < 0 || count is < 1 or > 200)
            throw new ClientError("Invalid library page.");
        return await client.BrowseAsync(await LibrarySpeakerAsync(groupId, ct), objectId, start, count, ct);
    }

    public async Task PlayAsync(int speakerId, string objectId, string selectionMode, CancellationToken ct)
    {
        var row = await db.Speakers.Include(x => x.Group).SingleAsync(x => x.Id == speakerId, ct);
        var gate = _locks.GetOrAdd(row.GroupId, _ => new(1));
        await gate.WaitAsync(ct);
        try {
            var selected = await LoadAsync(new Uri(row.DescriptionUrl), IPv4Subnet.Parse(row.Group.Subnet), ct);
            var target = await ResolveTargetAsync(selected, row.Group, ct);
            var tracks = await SelectAsync(target, objectId, selectionMode, time.GetLocalNow(), ct);
            await client.PlayQueueAsync(target, tracks, ct);
            await RefreshCoreAsync(row.GroupId, ct);
        } finally { gate.Release(); }
    }

    public async Task<string> PlaySelectionAsync(IReadOnlyList<(int SpeakerId, int? Volume)> speakers, int groupId,
        string objectId, string selectionMode, bool onlyIfIdle, DateTimeOffset occurrence, CancellationToken ct)
    {
        var gate = _locks.GetOrAdd(groupId, _ => new(1));
        await gate.WaitAsync(ct);
        try {
            var group = await db.Groups.SingleAsync(x => x.Id == groupId, ct);
            var subnet = IPv4Subnet.Parse(group.Subnet);
            var tracks = await SelectAsync(await LibrarySpeakerAsync(groupId, ct), objectId, selectionMode, occurrence, ct);
            var eligible = new List<(Speaker Speaker, int? Volume)>();
            foreach (var setting in speakers) {
                var row = await db.Speakers.SingleAsync(x => x.Id == setting.SpeakerId && x.GroupId == groupId, ct);
                try {
                    var selected = await LoadAsync(new Uri(row.DescriptionUrl), subnet, ct);
                    var target = await ResolveTargetAsync(selected, group, ct);
                    var status = await client.GetStatusAsync(selected, target, ct);
                    ApplyStatus(row, status);
                    if (!onlyIfIdle || status.State is "STOPPED" or "PAUSED_PLAYBACK" or "NO_MEDIA_PRESENT")
                        eligible.Add((selected, setting.Volume));
                } catch (Exception ex) when (ConnectionError(ex, ct)) { row.IsOnline = false; }
            }
            if (eligible.Count == 0) { await db.SaveChangesAsync(ct); return "Skipped: all selected rooms are playing or unavailable."; }
            var selectedIds = eligible.Select(x => x.Speaker.Id).ToHashSet();
            foreach (var member in eligible) {
                var topology = await client.GetGroupsAsync(member.Speaker, ct);
                // Fresh state again immediately before changing this room.
                if (onlyIfIdle) {
                    var target = await ResolveTargetAsync(member.Speaker, group, ct);
                    var state = (await client.GetStatusAsync(member.Speaker, target, ct)).State;
                    if (state is not ("STOPPED" or "PAUSED_PLAYBACK" or "NO_MEDIA_PRESENT"))
                        throw new ClientError("Playback began while preparing the schedule. No further rooms will be changed.");
                }
                await ExtractAsync(member.Speaker, topology, selectedIds, ct);
            }
            var coordinator = eligible[0].Speaker;
            foreach (var member in eligible.Skip(1))
                await client.JoinAsync(member.Speaker, coordinator, ct);
            foreach (var member in eligible)
                if (member.Volume is { } volume)
                    await client.SetVolumeAsync(member.Speaker, volume, ct);
            await client.PlayQueueAsync(coordinator, tracks, ct);
            await RefreshCoreAsync(groupId, ct);
            return $"Started {tracks.Count} track(s) in {string.Join(", ", eligible.Select(x => x.Speaker.Name))}.";
        } finally { gate.Release(); }
    }

    private async Task ExtractAsync(Speaker speaker, IReadOnlyList<SpeakerGroup> topology, HashSet<string> selected,
        CancellationToken ct)
    {
        var group = topology.First(x => x.Members.Any(m => m.Id == speaker.Id));
        if (group.CoordinatorId == speaker.Id) {
            var remaining = group.Members.FirstOrDefault(x => x.Visible && !selected.Contains(x.Id));
            if (remaining is not null)
                await client.DelegateAsync(speaker, remaining.Id, ct);
        }
        await client.StandaloneAsync(speaker, ct);
    }

    private async Task<Speaker> ResolveTargetAsync(Speaker speaker, NetworkGroup group, CancellationToken ct)
    {
        var topology = await client.GetGroupsAsync(speaker, ct);
        var liveGroup = topology.FirstOrDefault(x => x.Members.Any(m => m.Id == speaker.Id)) ?? throw new ClientError("Room is missing from live topology.");
        var coordinator = liveGroup.Members.First(x => x.Id == liveGroup.CoordinatorId);
        return coordinator.Id == speaker.Id ? speaker : await LoadAsync(coordinator.DescriptionUri, IPv4Subnet.Parse(group.Subnet), ct);
    }

    private async Task<Speaker> LibrarySpeakerAsync(int groupId, CancellationToken ct)
    {
        var group = await db.Groups.SingleOrDefaultAsync(x => x.Id == groupId, ct) ?? throw new ClientError("Subnet group not found.");
        var rows = await db.Speakers.Where(x => x.GroupId == groupId).OrderByDescending(x => x.IsOnline).ToListAsync(ct);
        foreach (var row in rows) {
            try { return await LoadAsync(new Uri(row.DescriptionUrl), IPv4Subnet.Parse(group.Subnet), ct); } catch (Exception ex) when (ConnectionError(ex, ct)) { }
        }
        throw new ClientError("No reachable speaker. Refresh this subnet group first.");
    }

    private async Task<IReadOnlyList<LibraryItem>> SelectAsync(Speaker speaker, string objectId, string mode, DateTimeOffset occurrence, CancellationToken ct)
    {
        ValidateObjectId(objectId);
        if (!SelectionModes.Contains(mode))
            throw new ClientError("Invalid music selection mode.");
        var root = await client.GetLibraryItemAsync(speaker, objectId, ct);
        if (!root.IsContainer)
            return IsNasTrack(root) ? [root] : throw new ClientError("Only NAS music library tracks are supported.");
        var budget = new LibraryBudget();
        if (mode is "RandomFolder" or "DayOfYearFolder") {
            var folders = (await ChildrenAsync(speaker, root.Id, budget, ct)).Where(x => x.IsContainer).OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
            var selections = new List<IReadOnlyList<LibraryItem>>();
            foreach (var folder in folders) {
                var contents = await TracksAsync(speaker, folder, budget, ct);
                if (contents.Count > 0)
                    selections.Add(contents);
            }
            if (selections.Count > 0)
                return selections[mode == "RandomFolder" ? Random.Shared.Next(selections.Count) : DayOfYearIndex(occurrence, selections.Count)];
        }
        var tracks = await TracksAsync(speaker, root, budget, ct);
        if (tracks.Count == 0)
            throw new ClientError("No playable NAS tracks in this selection.");
        return mode switch {
            "RandomTrack" => [tracks[Random.Shared.Next(tracks.Count)]],
            "DayOfYearTrack" => [tracks[DayOfYearIndex(occurrence, tracks.Count)]],
            _ => tracks,
        };
    }

    private async Task<List<LibraryItem>> TracksAsync(Speaker speaker, LibraryItem root, LibraryBudget budget, CancellationToken ct)
    {
        var result = new List<LibraryItem>();
        var pending = new Stack<(LibraryItem Item, int Depth)>();
        var visited = new HashSet<string>();
        pending.Push((root, 0));
        while (pending.TryPop(out var entry)) {
            if (!entry.Item.IsContainer) { if (IsNasTrack(entry.Item)) result.Add(entry.Item); continue; }
            if (!visited.Add(entry.Item.Id))
                continue;
            if (entry.Depth > 32)
                throw new ClientError("Library folder nesting exceeds 32 levels.");
            var children = await ChildrenAsync(speaker, entry.Item.Id, budget, ct);
            foreach (var child in children.OrderByDescending(x => x.Title, StringComparer.OrdinalIgnoreCase).ThenByDescending(x => x.Id, StringComparer.Ordinal))
                pending.Push((child, entry.Depth + 1));
            if (result.Count > 10000)
                throw new ClientError("Select a smaller folder (maximum 10,000 queued tracks).");
        }
        if (result.Count > 10000)
            throw new ClientError("Select a smaller folder (maximum 10,000 queued tracks).");
        return result;
    }

    private async Task<List<LibraryItem>> ChildrenAsync(Speaker speaker, string id, LibraryBudget budget, CancellationToken ct)
    {
        var result = new List<LibraryItem>();
        var start = 0;
        while (true) {
            var page = await client.BrowseAsync(speaker, id, start, 200, ct);
            budget.Entries += page.Items.Count;
            if (budget.Entries > 100000)
                throw new ClientError("Library selection exceeds 100,000 entries. Choose a smaller folder.");
            result.AddRange(page.Items);
            if (start + page.NumberReturned >= page.TotalMatches)
                break;
            if (page.NumberReturned == 0)
                throw new ClientError("The library changed during browsing. Please retry.");
            start += page.NumberReturned;
        }
        return result;
    }

    public static int DayOfYearIndex(DateTimeOffset date, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return (int)((long)(date.DayOfYear - 1) * (count - 1) / ((DateTime.IsLeapYear(date.Year) ? 366 : 365) - 1));
    }

    public static readonly IReadOnlySet<string> SelectionModes = new HashSet<string>(["PlayAll", "RandomTrack", "DayOfYearTrack", "RandomFolder", "DayOfYearFolder"], StringComparer.Ordinal);
    private sealed class LibraryBudget { public int Entries { get; set; } }
    private static bool IsNasTrack(LibraryItem item) =>
        !item.IsContainer && item.ResourceUri.StartsWith("x-file-cifs:", StringComparison.OrdinalIgnoreCase);
    private static void ValidateObjectId(string id)
    {
        if (id.Length > 2000 || !(id.StartsWith("A:", StringComparison.Ordinal) || id.StartsWith("S:", StringComparison.Ordinal)))
            throw new ClientError("Select an item from My Music Library or shared folders.");
    }
    private static Uri DescriptionUri(string address) =>
        new($"http://{address}:1400/xml/device_description.xml");
    private static bool ValidLocation(Uri uri, IPv4Subnet subnet) =>
        uri.Scheme == "http" && uri.Port == 1400 && string.IsNullOrEmpty(uri.UserInfo) && IPAddress.TryParse(uri.Host, out var ip) && subnet.Contains(ip);
    private async Task<Speaker> LoadAsync(Uri uri, IPv4Subnet subnet, CancellationToken ct)
    {
        if (!ValidLocation(uri, subnet))
            throw new ClientError("Speaker address is outside the configured subnet.");
        var speaker = await client.GetSpeakerAsync(uri, ct);
        if (!speaker.Id.StartsWith("RINCON_", StringComparison.OrdinalIgnoreCase) || !speaker.Services.ContainsKey("AVTransport") || !speaker.Services.ContainsKey("ZoneGroupTopology"))
            throw new SonosException("Device is not a controllable Sonos speaker.");
        return speaker;
    }
    private void ApplyStatus(DbSpeaker row, SpeakerStatus status)
    {
        row.State = status.State;
        row.Title = status.Title[..Math.Min(status.Title.Length, 1000)];
        row.Artist = status.Artist[..Math.Min(status.Artist.Length, 500)];
        row.Album = status.Album[..Math.Min(status.Album.Length, 500)];
        row.Volume = int.TryParse(status.Volume, out var volume) ? volume : 0;
        row.Muted = status.Muted;
        row.IsOnline = true;
        row.LastSeenAt = row.StatusUpdatedAt = time.GetUtcNow();
    }
    private static bool ConnectionError(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or SonosException or XmlException or UriFormatException || ex is OperationCanceledException && !ct.IsCancellationRequested;
}
