using System.Text;
using System.Text.Json;
using SonosController;

Console.OutputEncoding = Encoding.UTF8;
if (args is ["--help"] or ["-h"])
{
    Console.WriteLine("Sonos console controller\nUsage: dotnet run --project ConsoleApp2 -- [--settings <path>]\nDefault settings: appsettings.json beside the executable.");
    return 0;
}
if (args.Length != 0 && args is not ["--settings", _])
{
    Console.Error.WriteLine("Usage: dotnet run --project ConsoleApp2 -- [--settings <path>]");
    return 1;
}
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    var path = args.Length == 2 ? Path.GetFullPath(args[1]) : Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    var settings = AppSettings.Load(path);
    Console.WriteLine($"Settings: {path}");
    // Local speaker traffic must not go through a machine's configured HTTP proxy.
    using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(settings.HttpTimeoutSeconds)
    };
    var client = new SonosClient(http);
    var discovery = new SonosDiscovery(client, settings, message => Console.WriteLine($"  {message}"));
    await new ConsoleMenu(client, discovery, settings).RunAsync(cancellation.Token);
    return 0;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.WriteLine("\nGoodbye.");
    return 0;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException ||
                           SonosDiscovery.IsConnectionError(ex, cancellation.Token))
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}
