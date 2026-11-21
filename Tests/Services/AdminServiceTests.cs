using System.Security.Claims;
using AppServices;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using TestDb;

namespace Services;

public sealed class AdminServiceTests
{
    private static readonly Guid _tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _firstObjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid _secondObjectId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task GrantsOnlyImmutableEntraIdentifiersAndRejectsDuplicates()
    {
        using var fixture = new Fixture();
        var grant = await fixture.Service.AddAsync(_firstObjectId, CancellationToken.None);
        grant.TenantId.ShouldBe(_tenantId);
        grant.ObjectId.ShouldBe(_firstObjectId);
        var log = await fixture.Db.Logs.SingleAsync();
        log.Message.ShouldContain(_firstObjectId.ToString("D"));
        log.Message.ShouldContain(_tenantId.ToString("D"));
        log.Actor.ShouldBe($"{_tenantId:D}/{_secondObjectId:D}");
        log.Actor.ShouldNotContain("Display name");
        await Should.ThrowAsync<ClientError>(() => fixture.Service.AddAsync(_firstObjectId, CancellationToken.None));
        (await fixture.Db.Administrators.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task InvalidObjectAndTenantCannotCreateGrant()
    {
        using var fixture = new Fixture();
        await Should.ThrowAsync<ClientError>(() => fixture.Service.AddAsync(Guid.Empty, CancellationToken.None));
        fixture.Configuration["Entra:TenantId"] = "invalid";
        await Should.ThrowAsync<InvalidOperationException>(() => fixture.Service.AddAsync(_firstObjectId, CancellationToken.None));
        (await fixture.Db.Administrators.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task BootstrapCreatesOneGrantAndDoesNotRegrantAfterAdministratorsChange()
    {
        using var fixture = new Fixture();
        fixture.Configuration["Entra:BootstrapAdminObjectId"] = _firstObjectId.ToString("D");
        await fixture.Service.BootstrapAsync(CancellationToken.None);
        fixture.Configuration["Entra:BootstrapAdminObjectId"] = _secondObjectId.ToString("D");
        await fixture.Service.BootstrapAsync(CancellationToken.None);
        var grants = await fixture.Db.Administrators.ToListAsync();
        grants.ShouldHaveSingleItem();
        grants[0].ObjectId.ShouldBe(_firstObjectId);
    }

    [Fact]
    public async Task DeletePreservesLastAdministratorAndAuditsRevocation()
    {
        using var fixture = new Fixture();
        var first = await fixture.Service.AddAsync(_firstObjectId, CancellationToken.None);
        await Should.ThrowAsync<ClientError>(() => fixture.Service.DeleteAsync(first.Id, CancellationToken.None));
        await fixture.Service.AddAsync(_secondObjectId, CancellationToken.None);
        await fixture.Service.DeleteAsync(first.Id, CancellationToken.None);
        (await fixture.Db.Administrators.SingleAsync()).ObjectId.ShouldBe(_secondObjectId);
        var log = await fixture.Db.Logs.OrderByDescending(x => x.Id).FirstAsync();
        log.Message.ShouldStartWith("Revoked administrator access");
        log.Message.ShouldContain(_firstObjectId.ToString("D"));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        public TestDbContext Db { get; }
        public IConfigurationRoot Configuration { get; } = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Entra:TenantId"] = _tenantId.ToString("D")
        }).Build();
        public AdminService Service { get; }

        public Fixture()
        {
            _connection.Open();
            Db = new(_connection);
            Db.Database.EnsureCreated();
            var context = new DefaultHttpContext {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim("tid", _tenantId.ToString("D")),
                    new Claim("oid", _secondObjectId.ToString("D")),
                    new Claim(ClaimTypes.Name, "Display name")
                ], "Test"))
            };
            var audit = new AuditService(Db, TimeProvider.System, new HttpContextAccessor { HttpContext = context });
            Service = new(Db, audit, Configuration);
        }

        public void Dispose()
        {
            Db.Dispose();
            _connection.Dispose();
            ((IDisposable)Configuration).Dispose();
        }
    }
}
