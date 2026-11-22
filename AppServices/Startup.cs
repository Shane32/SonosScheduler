#pragma warning disable IDE0060 // Remove unused parameter
using AppServices.Scheduling;
using AppServices.Sonos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;

namespace AppServices;

public class Startup
{
    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // register services here
        services.AddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddScoped<AuditService>();
        services.AddScoped<AdminService>();
        services.AddScoped<IAuthorizationHandler, AdministratorAuthorizationHandler>();
        services.AddScoped<DatabaseStartupService>();
        services.AddSingleton<ExceptionLogService>();
        services.AddHttpClient<SonosClient>(client => client.Timeout = TimeSpan.FromSeconds(5))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false });
        services.AddScoped<SonosService>();
        services.AddHostedService<ScheduleWorker>();
        services.AddHostedService<MaintenanceWorker>();
    }
}
