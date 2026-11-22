namespace AppGraphQL.InputModels;

public sealed class GroupInput
{
    public required string Name { get; set; }
    public required string Subnet { get; set; }
    public string? SeedAddress { get; set; }
}
