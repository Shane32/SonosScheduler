using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace Infrastructure;

public sealed class SubApplicationTests : EntraTestBase
{
    protected override void ConfigureWebHostBuilder(IWebHostBuilder webHostBuilder)
    {
        base.ConfigureWebHostBuilder(webHostBuilder);
        webHostBuilder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, SubApplicationStartupFilter>());
    }

    protected override string GetGraphQLPath() => "/music/api/graphql";

    [Fact]
    public async Task PublicAuthenticationConfigurationUsesApplicationPath()
    {
        using var response = await Client.GetAsync("/music/api/auth/config", CancellationToken.None);
        response.EnsureSuccessStatusCode();
        var configuration = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        configuration.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        configuration.GetProperty("clientId").GetString().ShouldBe(ClientId.ToString("D"));
        configuration.GetProperty("authority").GetString().ShouldBe(Issuer);
        configuration.GetProperty("redirectUri").GetString().ShouldBe("/music/oauth/callback");
        configuration.GetProperty("logoutRedirectUri").GetString().ShouldBe("/music/oauth/logout");
        configuration.GetProperty("requireSignIn").GetBoolean().ShouldBeTrue();
        configuration.GetProperty("scopes").GetString().ShouldBe($"openid profile offline_access api://{ClientId:D}/access_as_user");
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
    }

    [Fact]
    public async Task BearerAuthenticationWorksUnderApplicationPathAndPasswordLoginIsAbsent()
    {
        using var result = await ReadGraphQLAsync("{ session { isAdmin name } }", CreateToken());
        result.RootElement.GetProperty("data").GetProperty("session").GetProperty("name").GetString().ShouldBe("Entra test user");
        Client.DefaultRequestHeaders.Add("X-Sonos-Request", "1");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken());
        using var login = await Client.PostAsJsonAsync("/music/api/admin/login", new { Name = "unused", Password = "unused" }, CancellationToken.None);
        login.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private sealed class SubApplicationStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
            => app => {
                app.UsePathBase("/music");
                next(app);
            };
    }
}
