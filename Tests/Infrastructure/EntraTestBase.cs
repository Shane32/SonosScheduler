using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using AppServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using TestDb;

namespace Infrastructure;

public abstract class EntraTestBase : TestBase
{
    protected static Guid TenantId { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
    protected static Guid ClientId { get; } = Guid.Parse("22222222-2222-2222-2222-222222222222");
    protected static Guid ObjectId { get; } = Guid.Parse("33333333-3333-3333-3333-333333333333");
    protected static string Issuer => $"https://login.microsoftonline.com/{TenantId:D}/v2.0";
    protected RsaSecurityKey SigningKey { get; } = CreateSigningKey();

    protected override void ConfigureWebHostBuilder(IWebHostBuilder webHostBuilder)
    {
        // The shared GraphQL helper relaxes JWT validation. These tests must use the real middleware rules.
        webHostBuilder.UseEnvironment("Test").UseTestServer().UseStartup<Startup>();
        webHostBuilder.ConfigureAppConfiguration((_, builder) => builder.AddInMemoryCollection(new Dictionary<string, string?> {
            ["Authentication:DefaultScheme"] = JwtBearerDefaults.AuthenticationScheme,
            ["Authentication:Schemes:Bearer:Authority"] = Issuer,
            ["Authentication:Schemes:Bearer:ValidAudience"] = ClientId.ToString("D"),
            ["Authentication:Schemes:Bearer:ValidateIssuer"] = "true",
            ["Sonos:RequireSignIn"] = "true"
        }));
        webHostBuilder.ConfigureTestServices(services => {
            services.AddSingleton<TimeProvider>(TimeProvider);
            services.AddSingleton(_ => {
                var connection = new SqliteConnection("Data Source=:memory:");
                connection.Open();
                return connection;
            });
            services.AddSingleton<AppDbContext>(provider => {
                var db = new TestDbContext(provider.GetRequiredService<SqliteConnection>());
                db.Database.EnsureCreated();
                db.ResetDbTrace();
                return db;
            });
            foreach (var hostedService in services.Where(x => x.ServiceType == typeof(IHostedService)).ToList())
                services.Remove(hostedService);
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options => {
                // Only replace key discovery. Production token and identity validation stays active.
                var configuration = new OpenIdConnectConfiguration {
                    Issuer = Issuer,
                    TokenEndpoint = $"https://login.microsoftonline.com/{TenantId:D}/oauth2/v2.0/token"
                };
                configuration.SigningKeys.Add(new RsaSecurityKey(new RSAParameters {
                    Modulus = SigningKey.Parameters.Modulus,
                    Exponent = SigningKey.Parameters.Exponent
                }) { KeyId = SigningKey.KeyId });
                options.Configuration = configuration;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });
        });
    }

    protected static RsaSecurityKey CreateSigningKey()
    {
        using var rsa = RSA.Create(2048);
        return new RsaSecurityKey(rsa.ExportParameters(true)) { KeyId = Guid.NewGuid().ToString("N") };
    }

    protected static List<Claim> CreateClaims() =>
        [
            new("tid", TenantId.ToString("D")),
            new("oid", ObjectId.ToString("D")),
            new("name", "Entra test user")
        ];

    protected string CreateToken(IEnumerable<Claim>? claims = null, string? issuer = null, string? audience = null, SecurityKey? signingKey = null, bool expired = false, bool omitSignature = false)
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer ?? Issuer,
            audience ?? ClientId.ToString("D"),
            claims ?? CreateClaims(),
            now.AddHours(-2),
            expired ? now.AddHours(-1) : now.AddMinutes(10),
            omitSignature ? null : new SigningCredentials(signingKey ?? SigningKey, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    protected async Task<HttpResponseMessage> PostGraphQLAsync(string query, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, GetGraphQLPath()) { Content = JsonContent.Create(new { query }) };
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Client.SendAsync(request, CancellationToken.None);
    }

    protected async Task<JsonDocument> ReadGraphQLAsync(string query, string token)
    {
        using var response = await PostGraphQLAsync(query, token);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken.None));
    }
}
