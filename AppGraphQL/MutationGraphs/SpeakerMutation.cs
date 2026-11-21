using AppServices.Sonos;

namespace AppGraphQL.MutationGraphs;

public sealed class SpeakerMutation(SonosService sonos) : DIObjectGraphBase
{
    public async Task<bool> RefreshAsync([Id] int id, CancellationToken ct) { await sonos.RefreshStatusAsync(id, ct); return true; }
    public async Task<bool> ControlAsync([Id] int id, string action, int? volume, CancellationToken ct) { await sonos.ControlAsync(id, action, volume, ct); return true; }
    public async Task<bool> SetMembersAsync([Id] int coordinatorId, [Id] int[] speakerIds, CancellationToken ct) { await sonos.SetMembersAsync(coordinatorId, speakerIds, ct); return true; }
    public async Task<bool> PlayAsync([Id] int id, string objectId, string selectionMode, CancellationToken ct) { await sonos.PlayAsync(id, objectId, selectionMode, ct); return true; }
}
