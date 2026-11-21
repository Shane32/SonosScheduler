namespace TestDb.Seeds;

internal sealed class NetworkSeed : Seed<NetworkGroup>
{
    public override Task SeedAsync(TestDbContext db, CancellationToken cancellationToken = default)
    {
        db.Groups.Add(new NetworkGroup {
            Id = 1,
            Name = "Home",
            Subnet = "192.168.1.0/24",
            SeedAddress = "192.168.1.20",
            Speakers = [new Speaker { Id = 1, SonosId = "RINCON_KITCHEN", Name = "Kitchen", Address = "192.168.1.20", DescriptionUrl = "http://192.168.1.20:1400/xml/device_description.xml", CoordinatorId = "RINCON_KITCHEN", IsOnline = true, State = "STOPPED" },
                new Speaker { Id = 2, SonosId = "RINCON_OFFICE", Name = "Office", Address = "192.168.1.21", DescriptionUrl = "http://192.168.1.21:1400/xml/device_description.xml", CoordinatorId = "RINCON_OFFICE", IsOnline = true, State = "PLAYING", Title = "Morning" }]
        });
        return Task.CompletedTask;
    }
}
