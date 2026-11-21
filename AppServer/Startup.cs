using System.Security.Claims;
using System.Threading.RateLimiting;
using AppGraphQL;
using AppServices;
using GraphQL.AspNetCore3;
using GraphQL.Types;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;

namespace AppServer;

public sealed class Startup(IConfiguration configuration)
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options => {
            options.Cookie.Name = "SonosScheduler.Admin";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
            options.Events.OnValidatePrincipal = async ctx => {
                var id = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var stamp = ctx.Principal?.FindFirstValue("stamp");
                var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                if (!int.TryParse(id, out var adminId) || !await db.Admins.AnyAsync(x => x.Id == adminId && x.SecurityStamp == stamp, ctx.HttpContext.RequestAborted)) {
                    ctx.RejectPrincipal();
                    await ctx.HttpContext.SignOutAsync();
                }
            };
        });
        services.AddAuthorization(ConfigureAuthorizationOptions);
        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(configuration.GetConnectionString(nameof(AppDbContext))));
        services.AddHttpContextAccessor();
        services.AddSingleton(configuration);
        services.AddAutoMapper((provider, config) => config.AddProfile(provider.GetRequiredService<AppGraphQL.AutoMapper.AutoMapperProfile>()), typeof(Program).Assembly);
        services.AddRouting();
        services.AddControllers();
        services.AddRateLimiter(options => {
            options.RejectionStatusCode = 429;
            options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        services.AddResponseCompression(options => {
            options.EnableForHttps = true;
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Append("application/graphql-response+json");
        });
        AppServices.Startup.ConfigureServices(services);
        AppGraphQL.Startup.ConfigureGraphQL(services, configuration);
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.UseResponseCompression();
        app.Use(async (context, next) => {
            if (context.Request.Path.StartsWithSegments("/api") && HttpMethods.IsPost(context.Request.Method)) {
                // A custom header forces cross-origin callers through a CORS preflight.
                if (context.Request.Headers["X-Sonos-Request"] != "1") { context.Response.StatusCode = 400; return; }
                if (context.Request.Headers.TryGetValue("Origin", out var origin) &&
                    (!Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var uri) || uri.Scheme != context.Request.Scheme || !string.Equals(uri.Authority, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase))) {
                    context.Response.StatusCode = 403;
                    return;
                }
            }
            await next(context);
        });
        if (!env.IsDevelopment())
            app.UseHsts();
        var staticPath = Path.Combine(env.WebRootPath ?? "wwwroot", "static");
        if (Directory.Exists(staticPath))
            app.UseCompressedStaticFiles(new() {
                FileSystemPath = staticPath,
                RequestPath = "/static",
                OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable",
            });
        app.MapWhen(context => !context.Request.Path.StartsWithSegments("/api") && !context.Request.Path.StartsWithSegments("/static"), spaApp => {
            spaApp.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache" });
            spaApp.UseSpa(spa => {
                spa.Options.SourcePath = env.WebRootPath;
                spa.Options.DefaultPage = "/index.html";
                spa.Options.DefaultPageStaticFileOptions = new() { OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache" };
                if (env.IsDevelopment())
                    spa.UseProxyToSpaDevelopmentServer("http://localhost:5173");
            });
        });
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseGraphQL("/api/graphql", options => {
            options.AuthorizationRequired = false;
            options.HandleGet = false;
            options.EnableBatchedRequests = false;
            options.ReadQueryStringOnPost = true;
            options.ValidationErrorsReturnBadRequest = false;
            options.CsrfProtectionEnabled = true;
            options.ReadFormOnPost = false;
        });
        if (env.IsDevelopment())
            app.UseGraphQLGraphiQL("/api/graphql", new() { GraphQLEndPoint = "/api/graphql" });
        app.UseEndpoints(endpoints => endpoints.MapControllers());
    }

    public async Task RunInitializationTestsAsync(IServiceProvider provider, CancellationToken ct = default)
    {
        provider.GetRequiredService<ISchema>().Initialize();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await scope.ServiceProvider.GetRequiredService<DatabaseStartupService>().MigrateAsync(ct);
        var interrupted = await db.ScheduleRuns.Where(x => x.Status == "Running").ToListAsync(ct);
        foreach (var run in interrupted) {
            run.Status = "Interrupted";
            run.CompletedAt = provider.GetRequiredService<TimeProvider>().GetUtcNow();
            run.Message = "Application restarted after this occurrence was claimed; it will not be retried.";
            scope.ServiceProvider.GetRequiredService<AuditService>().Add("Schedule", run.Message, run.ScheduleId, "Warning");
        }
        await db.SaveChangesAsync(ct);
        await scope.ServiceProvider.GetRequiredService<AdminService>().BootstrapAsync(configuration, ct);
    }

    internal static void ConfigureAuthorizationOptions(Microsoft.AspNetCore.Authorization.AuthorizationOptions options)
        => options.AddPolicy(SecurityPolicies.ADMIN_POLICY, builder => builder.RequireRole("Admin"));
}
