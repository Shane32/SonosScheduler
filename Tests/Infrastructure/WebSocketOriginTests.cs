using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace Infrastructure;

public sealed class WebSocketOriginTests : EntraTestBase
{
    protected override void ConfigureWebHostBuilder(IWebHostBuilder webHostBuilder)
    {
        base.ConfigureWebHostBuilder(webHostBuilder);
        webHostBuilder.ConfigureAppConfiguration((_, builder) => builder.AddInMemoryCollection(new Dictionary<string, string?> {
            ["Sonos:RequireSignIn"] = "false",
            ["Cors:AllowedOrigins"] = "https://trusted.example.invalid"
        }));
    }

    [Theory]
    [InlineData("https://other.example.invalid")]
    [InlineData("null")]
    [InlineData("invalid-origin")]
    public async Task AnonymousGraphQLRejectsUntrustedBrowserOriginBeforeUpgrade(string origin)
    {
        var client = Services.GetRequiredService<IServer>().ShouldBeOfType<TestServer>().CreateWebSocketClient();
        client.SubProtocols.Add("graphql-transport-ws");
        client.ConfigureRequest = request => request.Headers.Origin = origin;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var error = await Should.ThrowAsync<InvalidOperationException>(() => client.ConnectAsync(new Uri("ws://localhost/api/graphql"), timeout.Token));

        error.Message.ShouldContain("403");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://localhost")]
    [InlineData("https://trusted.example.invalid")]
    public async Task AnonymousGraphQLAcceptsLocalClientsAndExplicitlyAllowedBrowserOrigins(string? origin)
    {
        var client = Services.GetRequiredService<IServer>().ShouldBeOfType<TestServer>().CreateWebSocketClient();
        client.SubProtocols.Add("graphql-transport-ws");
        if (origin is not null)
            client.ConfigureRequest = request => request.Headers.Origin = origin;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var socket = await client.ConnectAsync(new Uri("ws://localhost/api/graphql"), timeout.Token);
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"type\":\"connection_init\"}").AsMemory(), WebSocketMessageType.Text, true, timeout.Token);
        var buffer = new byte[1024];
        var result = await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
        result.MessageType.ShouldBe(WebSocketMessageType.Text);
        using var message = JsonDocument.Parse(buffer.AsMemory(0, result.Count));
        message.RootElement.GetProperty("type").GetString().ShouldBe("connection_ack");
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Test completed.", timeout.Token);
    }
}
