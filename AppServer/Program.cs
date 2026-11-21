using Azure.Identity;

namespace AppServer;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
        if (builder.Environment.IsDevelopment())
            builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true);
        builder.Configuration.AddEnvironmentVariables();

        // Key Vault is optional; local installations need no Azure credential.
        var keyVaultName = builder.Configuration["KeyVaultName"];
        if (!string.IsNullOrWhiteSpace(keyVaultName)) {
            builder.Configuration.AddAzureKeyVault(
                new Uri($"https://{keyVaultName}.vault.azure.net/"),
#if DEBUG
                new VisualStudioCredential());
#else
                new DefaultAzureCredential());
#endif
        }

        builder.Host.UseDefaultServiceProvider(options => { options.ValidateScopes = true; options.ValidateOnBuild = true; });
        var startup = new Startup(builder.Configuration);
        startup.ConfigureServices(builder.Services);
        var app = builder.Build();
        if (!app.Environment.IsEnvironment("Test"))
            await startup.RunInitializationTestsAsync(app.Services);
        startup.Configure(app, app.Environment);
        await app.RunAsync();
    }
}
