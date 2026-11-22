namespace AppDb.Models;

[Index(nameof(TenantId), nameof(ObjectId), IsUnique = true)]
[Table("AdministratorGrants")]
public class AdministratorGrant
{
    public int Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ObjectId { get; set; }
    [MaxLength(200)]
    public string DisplayName { get; set; } = "";
}
