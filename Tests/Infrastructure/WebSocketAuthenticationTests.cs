using System.Security.Claims;
using System.Text.Json;
using AppGraphQL;
using GraphQL.AspNetCore3.WebSockets;
using GraphQL.Transport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Moq;

namespace Infrastructure;

public sealed class WebSocketAuthenticationTests : EntraTestBase
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConnectionInitializationUsesDatabaseAdministratorGrants(bool granted)
    {
        if (granted) {
            Db.Administrators.Add(new AdministratorGrant { TenantId = TenantId, ObjectId = ObjectId, DisplayName = "Previous name" });
            await Db.SaveChangesAsync();
        }
        var claims = CreateClaims();
        claims.AddRange([new("roles", "Admin"), new(ClaimTypes.Role, "Admin"), new("DbUserId", "untrusted")]);

        var context = await AuthenticateConnectionAsync(CreateToken(claims));

        var identity = context.User.Identity.ShouldNotBeNull();
        identity.IsAuthenticated.ShouldBeTrue();
        identity.Name.ShouldBe("Entra test user");
        context.User.IsInRole("Admin").ShouldBe(granted);
        context.User.HasClaim(x => x.Type == "DbUserId").ShouldBeFalse();
        if (granted)
            (await Db.Administrators.SingleAsync()).DisplayName.ShouldBe("Entra test user");
    }

    [Fact]
    public async Task ConnectionInitializationCannotBypassDatabaseIdentityValidation()
    {
        var claims = CreateClaims();
        claims.RemoveAll(x => x.Type == "oid");

        var context = await AuthenticateConnectionAsync(CreateToken(claims));

        (context.User.Identity?.IsAuthenticated ?? false).ShouldBeFalse();
        context.User.IsInRole("Admin").ShouldBeFalse();
    }

    [Fact]
    public async Task ExistingConnectionLosesAdministratorAccessWhenDatabaseGrantIsRevoked()
    {
        var grant = new AdministratorGrant { TenantId = TenantId, ObjectId = ObjectId };
        Db.Administrators.Add(grant);
        await Db.SaveChangesAsync();
        var context = await AuthenticateConnectionAsync(CreateToken());
        using var scope = Services.CreateScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        (await authorization.AuthorizeAsync(context.User, null, SecurityPolicies.ADMIN_POLICY)).Succeeded.ShouldBeTrue();

        Db.Administrators.Remove(grant);
        await Db.SaveChangesAsync();

        context.User.IsInRole("Admin").ShouldBeTrue();
        (await authorization.AuthorizeAsync(context.User, null, SecurityPolicies.ADMIN_POLICY)).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task ExistingConnectionLosesAdministratorAccessWhenItsValidatedTokenExpires()
    {
        Db.Administrators.Add(new AdministratorGrant { TenantId = TenantId, ObjectId = ObjectId });
        await Db.SaveChangesAsync();
        var context = await AuthenticateConnectionAsync(CreateToken());
        using var scope = Services.CreateScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        (await authorization.AuthorizeAsync(context.User, null, SecurityPolicies.ADMIN_POLICY)).Succeeded.ShouldBeTrue();
        var identity = (ClaimsIdentity)context.User.Identity.ShouldNotBeNull();
        identity.RemoveClaim(identity.FindFirst("exp").ShouldNotBeNull());
        // Simulate this already validated connection reaching its expiry without waiting for a real token lifetime.
        identity.AddClaim(new Claim("exp", DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));

        identity.IsAuthenticated.ShouldBeTrue();
        context.User.IsInRole("Admin").ShouldBeTrue();
        (await authorization.AuthorizeAsync(context.User, null, SecurityPolicies.ADMIN_POLICY)).Succeeded.ShouldBeFalse();
    }

    private async Task<DefaultHttpContext> AuthenticateConnectionAsync(string token)
    {
        using var scope = Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var connection = new Mock<IWebSocketConnection>();
        connection.SetupGet(x => x.HttpContext).Returns(context);
        var request = new AuthenticationRequest(connection.Object, "graphql-transport-ws", new OperationMessage {
            Type = "connection_init",
            Payload = JsonSerializer.SerializeToElement(new { Authorization = "Bearer " + token })
        }, []);

        await Services.GetRequiredService<IWebSocketAuthenticationService>().AuthenticateAsync(request);
        return context;
    }
}
