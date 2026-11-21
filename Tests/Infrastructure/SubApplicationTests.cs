using System.Net.Http.Json;
using AppServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace Infrastructure;

public sealed class SubApplicationTests : TestBase
{
    protected override void ConfigureWebHostBuilder(IWebHostBuilder webHostBuilder)
    {
        base.ConfigureWebHostBuilder(webHostBuilder);
        webHostBuilder.ConfigureTestServices(services => {
            services.AddSingleton<IStartupFilter, SubApplicationStartupFilter>();
            services.AddSingleton<IDataProtectionProvider, EphemeralDataProtectionProvider>();
        });
    }

    protected override string GetGraphQLPath() => "/music/api/graphql";

    [Fact]
    public async Task ApiAndAdministratorCookieUseApplicationPath()
    {
        await Services.GetRequiredService<AdminService>().AddAsync("test-admin", "test-password-123", CancellationToken.None);
        Client.DefaultRequestHeaders.Add("X-Sonos-Request", "1");
        using var response = await Client.PostAsJsonAsync("/music/api/admin/login", new { Name = "test-admin", Password = "test-password-123" }, CancellationToken.None);
        response.EnsureSuccessStatusCode();
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        cookie.ShouldContain("path=/music");
        cookie.ShouldContain("httponly");
        cookie.ShouldContain("samesite=strict");
        var result = await RunQueryAsync("{ __typename }");
        result.Errors.ShouldBeNull();
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
