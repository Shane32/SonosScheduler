using Microsoft.AspNetCore.Authorization;

namespace AppServices;

/// <summary>
/// Rechecks administrator access for each operation, including an existing WebSocket connection.
/// </summary>
public sealed class AdministratorAuthorizationHandler(AppDbContext db) : AuthorizationHandler<AdministratorAuthorizationHandler.Requirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, Requirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(context.User.FindFirst("tid")?.Value, out var tenantId) ||
            !Guid.TryParse(context.User.FindFirst("oid")?.Value, out var objectId) ||
            !long.TryParse(context.User.FindFirst("exp")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAt) ||
            expiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            return;

        if (await db.Administrators.AnyAsync(x => x.TenantId == tenantId && x.ObjectId == objectId))
            context.Succeed(requirement);
    }

    public sealed class Requirement : IAuthorizationRequirement;
}
