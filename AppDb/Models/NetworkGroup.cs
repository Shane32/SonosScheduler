namespace AppDb.Models;

public class NetworkGroup
{
    public int Id { get; set; }
    [MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(32)] public string Subnet { get; set; } = "";
    [MaxLength(45)] public string? SeedAddress { get; set; }
    public DateTimeOffset? LastRefreshedAt { get; set; }
    [MaxLength(2000)] public string? LastError { get; set; }
    public List<Speaker> Speakers { get; set; } = [];
}
