using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Infrastructure;

public sealed class OAuthTests : EntraTestBase
{
    private const string CODE_VERIFIER = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG";
    private readonly AssertionCredential _credential = new();
    private readonly OAuthMessageHandler _handler = new();
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();

    protected override void ConfigureWebHostBuilder(IWebHostBuilder webHostBuilder)
    {
        base.ConfigureWebHostBuilder(webHostBuilder);
        webHostBuilder.ConfigureAppConfiguration((_, builder) => builder.AddInMemoryCollection(new Dictionary<string, string?> {
            ["AZURE_CLIENT_ID"] = "55555555-5555-5555-5555-555555555555"
        }));
        webHostBuilder.ConfigureTestServices(services => {
            _httpClientFactory.Setup(x => x.CreateClient("MicrosoftOAuth")).Returns(() => new HttpClient(_handler, false));
            services.AddSingleton(_httpClientFactory.Object);
            services.AddSingleton<TokenCredential>(_credential);
        });
    }

    [Fact]
    public async Task PublicConfigurationAdvertisesLocalTokenExchangeWhenManagedIdentityIsConfigured()
    {
        using var response = await Client.GetAsync("/api/oauth/config", CancellationToken.None);
        response.EnsureSuccessStatusCode();
        var configuration = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        configuration.GetProperty("tokenEndpoint").GetString().ShouldBe("/api/oauth/token");
        configuration.GetProperty("scopes").GetString().ShouldBe("openid profile offline_access");
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        configuration.TryGetProperty("clientSecret", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData(200)]
    [InlineData(400)]
    public async Task AuthorizationCodeExchangeUsesManagedIdentityAssertionAndPreservesProviderResponse(int statusCode)
    {
        const string providerBody = "{\"result\":\"provider response\"}";
        _handler.ResponseStatusCode = (HttpStatusCode)statusCode;
        _handler.ResponseBody = providerBody;
        using var response = await PostTokenAsync(CreateAuthorizationCodeFields());
        response.StatusCode.ShouldBe((HttpStatusCode)statusCode);
        (await response.Content.ReadAsStringAsync(CancellationToken.None)).ShouldBe(providerBody);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        response.Headers.Pragma.ToString().ShouldContain("no-cache");
        _handler.RequestUri.ShouldBe(new Uri($"https://login.microsoftonline.com/{TenantId:D}/oauth2/v2.0/token"));
        var sent = ParseForm(_handler.RequestBody.ShouldNotBeNull());
        sent["grant_type"].ShouldBe("authorization_code");
        sent["client_id"].ShouldBe(ClientId.ToString("D"));
        sent["code"].ShouldBe("authorization-code");
        sent["code_verifier"].ShouldBe(CODE_VERIFIER);
        sent["redirect_uri"].ShouldBe("http://localhost/oauth/callback");
        sent["client_assertion_type"].ShouldBe("urn:ietf:params:oauth:client-assertion-type:jwt-bearer");
        sent["client_assertion"].ShouldBe("managed-identity-assertion");
        sent.ContainsKey("client_secret").ShouldBeFalse();
        _credential.Scopes.ShouldBe(["api://AzureADTokenExchange/.default"]);
        _httpClientFactory.Verify(x => x.CreateClient("MicrosoftOAuth"), Times.Once);
    }

    [Fact]
    public async Task RefreshTokenExchangeForwardsOnlyAllowedFields()
    {
        using var response = await PostTokenAsync([
            new("grant_type", "refresh_token"),
            new("client_id", ClientId.ToString("D")),
            new("refresh_token", "refresh-token"),
            new("scope", "openid profile offline_access")
        ]);
        response.EnsureSuccessStatusCode();
        var sent = ParseForm(_handler.RequestBody.ShouldNotBeNull());
        sent["grant_type"].ShouldBe("refresh_token");
        sent["refresh_token"].ShouldBe("refresh-token");
        sent["scope"].ShouldBe("openid profile offline_access");
        sent.ContainsKey("code").ShouldBeFalse();
        sent.ContainsKey("code_verifier").ShouldBeFalse();
    }

    [Theory]
    [InlineData("grant_type", "password")]
    [InlineData("client_id", "44444444-4444-4444-4444-444444444444")]
    [InlineData("code", "")]
    [InlineData("code_verifier", "short")]
    [InlineData("redirect_uri", "https://other.example.invalid/oauth/callback")]
    [InlineData("redirect_uri", "https://localhost/oauth/callback")]
    [InlineData("redirect_uri", "http://localhost:1234/oauth/callback")]
    [InlineData("client_secret", "untrusted-client-secret")]
    public async Task InvalidTokenRequestIsRejectedBeforeContactingIdentityProvider(string field, string value)
    {
        var fields = CreateAuthorizationCodeFields();
        fields.RemoveAll(x => x.Key == field);
        fields.Add(new(field, value));
        using var response = await PostTokenAsync(fields);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        AssertIdentityProviderWasNotContacted();
    }

    [Fact]
    public async Task DuplicateTokenParameterIsRejected()
    {
        var fields = CreateAuthorizationCodeFields();
        fields.Add(new("client_id", ClientId.ToString("D")));
        using var response = await PostTokenAsync(fields);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        AssertIdentityProviderWasNotContacted();
    }

    [Fact]
    public async Task JsonTokenRequestIsRejected()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/oauth/token") {
            Content = JsonContent.Create(new { grant_type = "authorization_code" })
        };
        using var response = await Client.SendAsync(request, CancellationToken.None);
        response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        AssertIdentityProviderWasNotContacted();
    }

    [Fact]
    public async Task TokenExchangeWithoutManagedIdentityIsUnavailable()
    {
        Services.GetRequiredService<IConfiguration>()["AZURE_CLIENT_ID"] = "";
        using var response = await PostTokenAsync(CreateAuthorizationCodeFields());
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        AssertIdentityProviderWasNotContacted();
    }

    private async Task<HttpResponseMessage> PostTokenAsync(IEnumerable<KeyValuePair<string, string>> fields)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/oauth/token") { Content = new FormUrlEncodedContent(fields) };
        return await Client.SendAsync(request, CancellationToken.None);
    }

    private static List<KeyValuePair<string, string>> CreateAuthorizationCodeFields() =>
        [
            new("grant_type", "authorization_code"),
            new("client_id", ClientId.ToString("D")),
            new("code", "authorization-code"),
            new("code_verifier", CODE_VERIFIER),
            new("redirect_uri", "http://localhost/oauth/callback")
        ];

    private static Dictionary<string, string> ParseForm(string body) =>
        body.Split('&').Select(x => x.Split('=', 2)).ToDictionary(x => Uri.UnescapeDataString(x[0].Replace('+', ' ')), x => Uri.UnescapeDataString(x[1].Replace('+', ' ')), StringComparer.Ordinal);

    private void AssertIdentityProviderWasNotContacted()
    {
        _credential.Scopes.ShouldBeNull();
        _handler.RequestUri.ShouldBeNull();
        _httpClientFactory.Verify(x => x.CreateClient(It.IsAny<string>()), Times.Never);
    }

    private sealed class AssertionCredential : TokenCredential
    {
        public string[]? Scopes { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Scopes = requestContext.Scopes;
            return new AccessToken("managed-identity-assertion", DateTimeOffset.UtcNow.AddMinutes(5));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class OAuthMessageHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? RequestBody { get; private set; }
        public HttpStatusCode ResponseStatusCode { get; set; } = HttpStatusCode.OK;
        public string ResponseBody { get; set; } = "{\"id_token\":\"id-token\"}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(ResponseStatusCode) { Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json") };
        }
    }
}
