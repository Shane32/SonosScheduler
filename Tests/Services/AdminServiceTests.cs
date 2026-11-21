using System.Security.Claims;
using AppServer;
using AppServices;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
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
        grant.DisplayName.ShouldBe("");
        var log = await fixture.Db.Logs.SingleAsync();
        log.Message.ShouldContain(_firstObjectId.ToString("D"));
        log.Message.ShouldContain(_tenantId.ToString("D"));
        log.Actor.ShouldBe($"{_tenantId:D}/{_secondObjectId:D}");
        log.Actor.ShouldNotContain("Display name");
        await Should.ThrowAsync<ClientError>(() => fixture.Service.AddAsync(_firstObjectId, CancellationToken.None));
        (await fixture.Db.Administrators.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task StoresTrimmedAdministratorDisplayName()
    {
        using var fixture = new Fixture();
        var grant = await fixture.Service.AddAsync(_firstObjectId, CancellationToken.None, "  Ada Lovelace  ");
        grant.DisplayName.ShouldBe("Ada Lovelace");
        (await fixture.Db.Administrators.SingleAsync()).DisplayName.ShouldBe("Ada Lovelace");
        (await fixture.Db.Logs.SingleAsync()).Message.ShouldNotContain("Ada Lovelace");
    }

    [Fact]
    public async Task OversizedDisplayNameCannotCreateGrant()
    {
        using var fixture = new Fixture();
        var error = await Should.ThrowAsync<ClientError>(() => fixture.Service.AddAsync(_firstObjectId, CancellationToken.None, new string('a', 201)));
        error.Message.ShouldBe("An administrator display name cannot exceed 200 characters.");
        (await fixture.Db.Administrators.CountAsync()).ShouldBe(0);
        (await fixture.Db.Logs.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task ValidatedTokenNameRefreshesExistingGrantWithoutChangingPermissionIdentifiers()
    {
        using var fixture = new Fixture();
        var grant = await fixture.Service.AddAsync(_firstObjectId, CancellationToken.None, "Previous display name");
        fixture.Db.ResetDbTrace();
        var identity = CreateIdentity(_firstObjectId, "  Ada Lovelace  ");
        (await fixture.ApplyIdentityAsync(identity)).ShouldBeTrue();

        identity.HasClaim(ClaimTypes.Role, "Admin").ShouldBeTrue();
        var updated = await fixture.Db.Administrators.AsNoTracking().SingleAsync();
        updated.Id.ShouldBe(grant.Id);
        updated.TenantId.ShouldBe(_tenantId);
        updated.ObjectId.ShouldBe(_firstObjectId);
        updated.DisplayName.ShouldBe("Ada Lovelace");
        fixture.Db.DbTrace.ShouldContain("UPDATE \"AdministratorGrants\"");

        fixture.Db.ResetDbTrace();
        (await fixture.ApplyIdentityAsync(CreateIdentity(_firstObjectId, "Ada Lovelace"))).ShouldBeTrue();
        fixture.Db.DbTrace.ShouldNotContain("UPDATE \"AdministratorGrants\"");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task MissingTokenNamePreservesExistingDisplayName(string? displayName)
    {
        using var fixture = new Fixture();
        await fixture.Service.AddAsync(_firstObjectId, CancellationToken.None, "Ada Lovelace");
        fixture.Db.ResetDbTrace();
        (await fixture.ApplyIdentityAsync(CreateIdentity(_firstObjectId, displayName))).ShouldBeTrue();
        (await fixture.Db.Administrators.SingleAsync()).DisplayName.ShouldBe("Ada Lovelace");
        fixture.Db.DbTrace.ShouldNotContain("UPDATE \"AdministratorGrants\"");
    }

    [Fact]
    public async Task MatchingDisplayNameCannotCreateOrInheritAdministratorGrant()
    {
        using var fixture = new Fixture();
        await fixture.Service.AddAsync(_firstObjectId, CancellationToken.None, "Ada Lovelace");
        var identity = CreateIdentity(_secondObjectId, "Ada Lovelace");
        identity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));
        (await fixture.ApplyIdentityAsync(identity)).ShouldBeTrue();
        identity.HasClaim(ClaimTypes.Role, "Admin").ShouldBeFalse();
        (await fixture.Db.Administrators.CountAsync()).ShouldBe(1);
        (await fixture.Db.Administrators.SingleAsync()).ObjectId.ShouldBe(_firstObjectId);
    }

    [Fact]
    public async Task InvalidObjectAndTenantCannotCreateGrant()
    {
        using var fixture = new Fixture();
        await Should.ThrowAsync<ClientError>(() => fixture.Service.AddAsync(Guid.Empty, CancellationToken.None));
        fixture.Configuration["Authentication:Schemes:Bearer:Authority"] = "https://login.microsoftonline.com/invalid/v2.0";
        await Should.ThrowAsync<InvalidOperationException>(() => fixture.Service.AddAsync(_firstObjectId, CancellationToken.None));
        (await fixture.Db.Administrators.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task BootstrapCreatesOneGrantAndDoesNotRegrantAfterAdministratorsChange()
    {
        using var fixture = new Fixture();
        fixture.Configuration["Administrators:BootstrapObjectId"] = _firstObjectId.ToString("D");
        await fixture.Service.BootstrapAsync(CancellationToken.None);
        fixture.Configuration["Administrators:BootstrapObjectId"] = _secondObjectId.ToString("D");
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

    private static ClaimsIdentity CreateIdentity(Guid objectId, string? displayName)
    {
        var identity = new ClaimsIdentity([
            new Claim("tid", _tenantId.ToString("D")),
            new Claim("oid", objectId.ToString("D"))
        ], "ValidatedEntraToken");
        if (displayName != null)
            identity.AddClaim(new Claim("name", displayName));
        return identity;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private readonly ServiceProvider _services;
        public TestDbContext Db { get; }
        public IConfigurationRoot Configuration { get; } = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Authentication:Schemes:Bearer:Authority"] = $"https://login.microsoftonline.com/{_tenantId:D}/v2.0"
        }).Build();
        public AdminService Service { get; }

        public Fixture()
        {
            _connection.Open();
            Db = new(_connection);
            Db.Database.EnsureCreated();
            _services = new ServiceCollection().AddSingleton<AppDbContext>(Db).AddSingleton<IConfiguration>(Configuration).BuildServiceProvider();
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

        public async Task<bool> ApplyIdentityAsync(ClaimsIdentity identity)
        {
            var options = new JwtBearerOptions { Authority = Configuration["Authentication:Schemes:Bearer:Authority"] };
            var context = new TokenValidatedContext(
                new DefaultHttpContext { RequestServices = _services },
                new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)),
                options) {
                Principal = new ClaimsPrincipal(identity)
            };
            await new DbAuthService(NullLogger<DbAuthService>.Instance).OnTokenValidatedAsync(context, CancellationToken.None);
            return context.Result?.Failure is null;
        }

        public void Dispose()
        {
            Db.Dispose();
            _services.Dispose();
            _connection.Dispose();
            ((IDisposable)Configuration).Dispose();
        }
    }
}
