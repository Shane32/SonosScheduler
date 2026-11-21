namespace AppGraphQL.QueryGraphs;

public sealed class SpeakerGraphType : EfObjectGraphType<AppDbContext, Speaker>
{
    public SpeakerGraphType()
    {
        EfIdField(x => x.Id);
        EfIdField(x => x.GroupId);
        EfField(x => x.SonosId);
        EfField(x => x.Name);
        EfField(x => x.Address);
        EfField(x => x.CoordinatorId);
        EfField(x => x.IsOnline);
        EfField(x => x.State);
        EfField(x => x.Title);
        EfField(x => x.Artist);
        EfField(x => x.Album);
        EfField(x => x.Volume);
        EfField(x => x.Muted);
        EfField(x => x.LastSeenAt);
        EfField(x => x.StatusUpdatedAt);
    }
}
