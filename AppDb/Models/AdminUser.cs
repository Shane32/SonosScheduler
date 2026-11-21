namespace AppDb.Models;

[Index(nameof(NormalizedName), IsUnique = true)]
public class AdminUser
{
    public int Id { get; set; }
    [MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(100)] public string NormalizedName { get; set; } = "";
    [MaxLength(500)] public string PasswordHash { get; set; } = "";
    [MaxLength(50)] public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
}
