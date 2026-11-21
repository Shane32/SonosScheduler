using AppServices.Scheduling;
using AppServices.Sonos;

namespace AppServices;

public static class Startup
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddScoped<AuditService>();
        services.AddScoped<AdminService>();
        services.AddScoped<DatabaseStartupService>();
        services.AddSingleton<ExceptionLogService>();
        services.AddHttpClient<SonosClient>(client => client.Timeout = TimeSpan.FromSeconds(5))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false });
        services.AddScoped<SonosService>();
        services.AddHostedService<ScheduleWorker>();
        services.AddHostedService<MaintenanceWorker>();
    }
}
