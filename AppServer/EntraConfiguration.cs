namespace AppServer;

public sealed record EntraConfiguration(string? TenantId, string? ClientId, bool RequireSignIn)
{
    public bool Enabled => TenantId is not null && ClientId is not null;
    public string? Authority => Enabled ? $"https://login.microsoftonline.com/{TenantId}/v2.0" : null;
    public string? Scopes => Enabled ? $"openid profile offline_access api://{ClientId}/access_as_user" : null;

    public static EntraConfiguration Read(IConfiguration configuration)
    {
        var tenant = configuration["Entra:TenantId"];
        var client = configuration["Entra:ClientId"];
        var requireSignIn = configuration.GetValue<bool>("Entra:RequireSignIn");
        if (string.IsNullOrWhiteSpace(tenant) && string.IsNullOrWhiteSpace(client)) {
            if (requireSignIn)
                throw new InvalidOperationException("Entra must be configured when sign-in is required.");
            return new(null, null, false);
        }
        if (!Guid.TryParse(tenant, out var tenantId) || tenantId == Guid.Empty || !Guid.TryParse(client, out var clientId) || clientId == Guid.Empty)
            throw new InvalidOperationException("Configure both Entra tenant and client IDs as valid GUIDs.");
        return new(tenantId.ToString("D"), clientId.ToString("D"), requireSignIn);
    }
}
