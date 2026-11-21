using System.Net;
using System.Security.Claims;

namespace Infrastructure;

public sealed class EntraAuthenticationTests : EntraTestBase
{
    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("signature")]
    [InlineData("unsigned")]
    [InlineData("expired")]
    [InlineData("missing-object")]
    [InlineData("invalid-object")]
    [InlineData("missing-tenant")]
    [InlineData("invalid-tenant")]
    [InlineData("wrong-tenant")]
    [InlineData("missing-scope")]
    [InlineData("wrong-scope")]
    public async Task InvalidTokenCannotAccessApi(string defect)
    {
        var claims = CreateClaims();
        string? issuer = null;
        string? audience = null;
        var key = SigningKey;
        switch (defect) {
            case "issuer":
                issuer = "https://issuer.invalid/v2.0";
                break;
            case "audience":
                audience = "44444444-4444-4444-4444-444444444444";
                break;
            case "signature":
                key = CreateSigningKey();
                key.KeyId = SigningKey.KeyId;
                break;
            case "unsigned":
                break;
            case "expired":
                break;
            case "missing-object":
                claims.RemoveAll(x => x.Type == "oid");
                break;
            case "invalid-object":
                claims.RemoveAll(x => x.Type == "oid");
                claims.Add(new("oid", "invalid"));
                break;
            case "missing-tenant":
                claims.RemoveAll(x => x.Type == "tid");
                break;
            case "invalid-tenant":
                claims.RemoveAll(x => x.Type == "tid");
                claims.Add(new("tid", "invalid"));
                break;
            case "wrong-tenant":
                claims.RemoveAll(x => x.Type == "tid");
                claims.Add(new("tid", "44444444-4444-4444-4444-444444444444"));
                break;
            case "missing-scope":
                claims.RemoveAll(x => x.Type == "scp");
                break;
            case "wrong-scope":
                claims.RemoveAll(x => x.Type == "scp");
                claims.Add(new("scp", "access_as_user.other"));
                break;
            default:
                throw new ArgumentException("Unknown test token defect.", nameof(defect));
        }
        using var response = await PostGraphQLAsync("{ session { isAdmin } }", CreateToken(claims, issuer, audience, key, defect == "expired", defect == "unsigned"));
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RequiredSignInRejectsAnonymousLanRequests()
    {
        using var response = await PostGraphQLAsync("{ session { isAdmin } }");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ValidIdentityCannotGrantItselfAdministratorAccessThroughTokenRoles()
    {
        var claims = CreateClaims();
        claims.AddRange([new("roles", "Admin"), new("role", "Admin"), new(ClaimTypes.Role, "Admin")]);
        var token = CreateToken(claims);
        using var session = await ReadGraphQLAsync("{ session { isAdmin name } }", token);
        var identity = session.RootElement.GetProperty("data").GetProperty("session");
        identity.GetProperty("name").GetString().ShouldBe("Entra test user");
        identity.GetProperty("isAdmin").GetBoolean().ShouldBeFalse();
        using var logs = await ReadGraphQLAsync("{ logs { totalCount } }", token);
        logs.RootElement.GetProperty("errors")[0].GetProperty("extensions").GetProperty("code").GetString().ShouldBe("ACCESS_DENIED");
        (await Db.Administrators.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task AdministratorListUsesValidatedTokenNameAndTracksNameChanges()
    {
        var grant = new AdministratorGrant { TenantId = TenantId, ObjectId = ObjectId, DisplayName = "Previous display name" };
        Db.Administrators.Add(grant);
        await Db.SaveChangesAsync();
        const string query = "{ admins { items { objectId displayName } } }";
        using var initial = await ReadGraphQLAsync(query, CreateToken());
        initial.RootElement.TryGetProperty("errors", out _).ShouldBeFalse();
        var first = initial.RootElement.GetProperty("data").GetProperty("admins").GetProperty("items")[0];
        first.GetProperty("objectId").GetString().ShouldBe(ObjectId.ToString("D"));
        first.GetProperty("displayName").GetString().ShouldBe("Entra test user");
        (await Db.Administrators.SingleAsync()).DisplayName.ShouldBe("Entra test user");

        var renamedClaims = CreateClaims();
        renamedClaims.RemoveAll(x => x.Type == "name");
        renamedClaims.Add(new("name", "Updated display name"));
        using var renamed = await ReadGraphQLAsync(query, CreateToken(renamedClaims));
        renamed.RootElement.TryGetProperty("errors", out _).ShouldBeFalse();
        renamed.RootElement.GetProperty("data").GetProperty("admins").GetProperty("items")[0]
            .GetProperty("displayName").GetString().ShouldBe("Updated display name");
        (await Db.Administrators.SingleAsync()).DisplayName.ShouldBe("Updated display name");
        (await Db.Logs.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task DatabaseGrantEnablesAdministratorAndRevocationAppliesToExistingToken()
    {
        var grant = new AdministratorGrant { TenantId = TenantId, ObjectId = ObjectId };
        Db.Administrators.Add(grant);
        await Db.SaveChangesAsync();
        var token = CreateToken();
        using var allowed = await ReadGraphQLAsync("{ session { isAdmin } logs { totalCount } }", token);
        allowed.RootElement.TryGetProperty("errors", out _).ShouldBeFalse();
        allowed.RootElement.GetProperty("data").GetProperty("session").GetProperty("isAdmin").GetBoolean().ShouldBeTrue();
        allowed.RootElement.GetProperty("data").GetProperty("logs").GetProperty("totalCount").GetInt32().ShouldBe(0);
        Db.Administrators.Remove(grant);
        await Db.SaveChangesAsync();
        using var revoked = await ReadGraphQLAsync("{ session { isAdmin } }", token);
        revoked.RootElement.GetProperty("data").GetProperty("session").GetProperty("isAdmin").GetBoolean().ShouldBeFalse();
        using var denied = await ReadGraphQLAsync("{ logs { totalCount } }", token);
        denied.RootElement.GetProperty("errors")[0].GetProperty("extensions").GetProperty("code").GetString().ShouldBe("ACCESS_DENIED");
    }

    [Fact]
    public async Task MatchingObjectIdInDifferentTenantDoesNotGrantAdministrator()
    {
        Db.Administrators.Add(new AdministratorGrant { TenantId = Guid.Parse("44444444-4444-4444-4444-444444444444"), ObjectId = ObjectId });
        await Db.SaveChangesAsync();
        using var response = await ReadGraphQLAsync("{ session { isAdmin } }", CreateToken());
        response.RootElement.GetProperty("data").GetProperty("session").GetProperty("isAdmin").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task GrantedAdministratorCanManageGuidGrantsAndAuditsUseImmutableIdentity()
    {
        Db.Administrators.Add(new AdministratorGrant { TenantId = TenantId, ObjectId = ObjectId });
        await Db.SaveChangesAsync();
        var targetObjectId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var claims = CreateClaims();
        claims.Add(new("preferred_username", "entra-test@example.invalid"));
        var token = CreateToken(claims);
        var addMutation = $$"""
            mutation {
              admins {
                add(objectId: "{{targetObjectId:D}}", displayName: "Other administrator") { id tenantId objectId displayName }
              }
            }
            """;
        using var added = await ReadGraphQLAsync(addMutation, token);
        added.RootElement.TryGetProperty("errors", out _).ShouldBeFalse();
        var returnedGrant = added.RootElement.GetProperty("data").GetProperty("admins").GetProperty("add");
        Guid.Parse(returnedGrant.GetProperty("tenantId").GetString()!).ShouldBe(TenantId);
        Guid.Parse(returnedGrant.GetProperty("objectId").GetString()!).ShouldBe(targetObjectId);
        returnedGrant.GetProperty("displayName").GetString().ShouldBe("Other administrator");
        var addedId = returnedGrant.GetProperty("id").GetString();
        addedId.ShouldNotBeNullOrEmpty();
        (await Db.Administrators.SingleAsync(x => x.ObjectId == targetObjectId)).Id.ToString(CultureInfo.InvariantCulture).ShouldBe(addedId);
        using var duplicate = await ReadGraphQLAsync(addMutation, token);
        duplicate.RootElement.GetProperty("errors")[0].GetProperty("message").GetString().ShouldBe("This Entra account is already an administrator.");
        (await Db.Administrators.CountAsync()).ShouldBe(2);
        using var deleted = await ReadGraphQLAsync($$"""mutation { admins { delete(id: "{{addedId}}") } }""", token);
        deleted.RootElement.TryGetProperty("errors", out _).ShouldBeFalse();
        deleted.RootElement.GetProperty("data").GetProperty("admins").GetProperty("delete").GetBoolean().ShouldBeTrue();
        (await Db.Administrators.SingleAsync()).ObjectId.ShouldBe(ObjectId);
        var logs = await Db.Logs.OrderBy(x => x.Id).ToListAsync();
        logs.Count.ShouldBe(2);
        logs[0].Message.ShouldStartWith("Granted administrator access");
        logs[1].Message.ShouldStartWith("Revoked administrator access");
        foreach (var log in logs) {
            log.Actor.ShouldBe($"{TenantId:D}/{ObjectId:D}");
            $"{log.Actor}\n{log.Message}\n{log.Details}".ShouldNotContain("Entra test user");
            $"{log.Actor}\n{log.Message}\n{log.Details}".ShouldNotContain("entra-test@example.invalid");
        }
    }

    [Fact]
    public async Task UnexpectedGraphQLExceptionIsLoggedWithoutDisplayNameOrUsername()
    {
        Db.Administrators.Add(new AdministratorGrant { TenantId = TenantId, ObjectId = ObjectId });
        await Db.SaveChangesAsync();
        var claims = CreateClaims();
        claims.Add(new("preferred_username", "entra-test@example.invalid"));
        using var response = await ReadGraphQLAsync("mutation { groups { refresh(id: \"9999\") } }", CreateToken(claims));
        response.RootElement.GetProperty("errors").GetArrayLength().ShouldBe(1);
        var log = await Db.Logs.SingleAsync();
        log.Category.ShouldBe("GraphQL");
        log.Level.ShouldBe("Error");
        log.Details.ShouldNotBeNull().ShouldContain("InvalidOperationException");
        log.Actor.ShouldBe($"{TenantId:D}/{ObjectId:D}");
        $"{log.Actor}\n{log.Message}\n{log.Details}".ShouldNotContain("Entra test user");
        $"{log.Actor}\n{log.Message}\n{log.Details}".ShouldNotContain("entra-test@example.invalid");
    }
}
