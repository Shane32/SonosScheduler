namespace AppDb.Models;

[Index(nameof(GroupId), nameof(SonosId), IsUnique = true)]
public class Speaker
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public NetworkGroup Group { get; set; } = null!;
    [MaxLength(100)] public string SonosId { get; set; } = "";
    [MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(45)] public string Address { get; set; } = "";
    [MaxLength(500)] public string DescriptionUrl { get; set; } = "";
    [MaxLength(100)] public string CoordinatorId { get; set; } = "";
    public bool IsOnline { get; set; }
    [MaxLength(50)] public string State { get; set; } = "UNKNOWN";
    [MaxLength(1000)] public string Title { get; set; } = "";
    [MaxLength(500)] public string Artist { get; set; } = "";
    [MaxLength(500)] public string Album { get; set; } = "";
    public int Volume { get; set; }
    public bool Muted { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? StatusUpdatedAt { get; set; }
}
