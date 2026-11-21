namespace AppServer;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true);
        if (builder.Environment.IsDevelopment())
            builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true);
        builder.Configuration.AddEnvironmentVariables();
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
