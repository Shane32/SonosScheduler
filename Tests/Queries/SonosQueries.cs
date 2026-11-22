namespace Queries;

public sealed class SonosQueries : TestBase
{
    [Fact]
    public async Task DashboardIsOpenToLan()
    {
        await Db.SeedAsync<NetworkGroup>();
        var response = await RunQueryAsync("""
            query {
              session { isAdmin name }
              groups { items { id name subnet speakers { id name isOnline state } } totalCount }
              schedules { items { id name } totalCount }
            }
            """);
        response.ShouldMatchApproved();
    }

    [Fact]
    public async Task LogsRequireAdministrator()
    {
        var response = await RunQueryAsync("query { logs { items { id message } } }");
        response.ShouldMatchApproved();
    }

    [Fact]
    public async Task AdminMutationsRequireAdministrator()
    {
        var response = await RunQueryAsync("""mutation { groups { add(input: { name: "No", subnet: "192.168.1.0/24" }) { id } } }""");
        response.ShouldMatchApproved();
        (await Db.Groups.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task UnexpectedPipelineExceptionIsPersisted()
    {
        await RunQueryAsync("mutation { speakers { refresh(id: \"999\") } }");
        var log = await Db.Logs.SingleAsync();
        log.Category.ShouldBe("GraphQL");
        log.Level.ShouldBe("Error");
        log.Details.ShouldNotBeNull().ShouldContain("InvalidOperationException");
    }
}
