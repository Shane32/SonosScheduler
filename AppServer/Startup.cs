using System.Diagnostics;
using AppGraphQL;
using AppServices;
using AutoMapper;
using AutoMapper.EquivalencyExpression;
using Azure.Core;
using Azure.Identity;
using GraphQL.AspNetCore3;
using GraphQL.Linq;
using GraphQL.Types;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.ResponseCompression;

namespace AppServer;

public sealed class Startup(IConfiguration configuration)
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Bearer authority, audience, and issuer validation use the template's Authentication settings.
        services.AddAuthentication(configuration["Authentication:DefaultScheme"] ?? JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => {
            options.MapInboundClaims = false;
            options.TokenValidationParameters.NameClaimType = "name";
            options.Events = new() {
                OnTokenValidated = async context => {
                    var dbAuthService = context.HttpContext.RequestServices.GetRequiredService<DbAuthService>();
                    await dbAuthService.OnTokenValidatedAsync(context, context.HttpContext.RequestAborted);
                }
            };
        });
        services.AddSingleton<DbAuthService>();
        services.AddAuthorization(ConfigureAuthorizationOptions);

        var origins = configuration.GetSection("Cors").GetValue<string>("AllowedOrigins")?.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries) ?? [];
        services.AddCors(options => options.AddDefaultPolicy(builder => builder.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader().AllowCredentials()));
        services.AddDbContext<AppDbContext>(options => {
#if DEBUG
            options.LogTo(message => Debug.WriteLine(message), LogLevel.Debug);
#endif
            options.UseSqlServer(configuration.GetConnectionString(nameof(AppDbContext)), sql => {
                if (AppDbContext.Schema != null)
                    sql.MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.Schema);
            });
        });
        services.AddHttpContextAccessor();
        services.AddSingleton(configuration);
        services.AddAutoMapper((provider, config) => {
            config.AddCollectionMappers();
            config.UseEntityFrameworkCoreModel<AppDbContext>(provider);
            config.ConstructServicesUsing(provider.GetRequiredService);
            config.AddProfile(provider.GetRequiredService<AppGraphQL.AutoMapper.AutoMapperProfile>());
        }, typeof(Program).Assembly);
        services.AddRouting();
        services.AddControllers();
        services.AddSingleton<TokenCredential>(provider => {
            var clientId = provider.GetRequiredService<IConfiguration>()["AZURE_CLIENT_ID"];
            if (string.IsNullOrWhiteSpace(clientId))
                return new DefaultAzureCredential();
            if (!Guid.TryParse(clientId, out _))
                throw new InvalidOperationException("AZURE_CLIENT_ID must be the runtime user-assigned managed identity's client ID.");
            return new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId));
        });
        services.AddHttpClient("MicrosoftOAuth", client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        services.AddHttpsRedirection(options => options.HttpsPort = 443);
        services.AddResponseCompression(options => {
            options.EnableForHttps = true;
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Append("application/graphql-response+json");
        });
        AppGraphQL.Startup.ConfigureGraphQL(services, configuration);
        AppServices.Startup.ConfigureServices(services);
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsProduction())
            app.UseHttpsRedirection();
        app.UseResponseCompression();
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
        app.UseWebSockets();
        var allowedOrigins = configuration.GetSection("Cors").GetValue<string>("AllowedOrigins")?.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries) ?? [];
        app.Use(async (context, next) => {
            if (context.WebSockets.IsWebSocketRequest && context.Request.Path.StartsWithSegments("/api/graphql") &&
                context.Request.Headers.TryGetValue("Origin", out var origin)) {
                var sameOrigin = Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var uri) &&
                    uri.Scheme == context.Request.Scheme && string.Equals(uri.Authority, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase);
                if (!sameOrigin && !allowedOrigins.Contains(origin.ToString(), StringComparer.OrdinalIgnoreCase)) {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
            }
            await next(context);
        });
        app.UseRouting();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseGraphQL("/api/graphql", options => {
            options.AuthorizationRequired = configuration.GetValue<bool>("Sonos:RequireSignIn");
            options.HandleGet = false;
            options.EnableBatchedRequests = false;
            options.ReadQueryStringOnPost = true;
            options.ValidationErrorsReturnBadRequest = false;
            options.CsrfProtectionEnabled = true;
            options.ReadFormOnPost = false;
            options.WebSockets.KeepAliveMode = GraphQL.AspNetCore3.WebSockets.KeepAliveMode.TimeoutWithPayload;
            options.WebSockets.KeepAliveTimeout = TimeSpan.FromSeconds(10);
            options.WebSockets.SupportedWebSocketSubProtocols = [GraphQL.AspNetCore3.WebSockets.GraphQLWs.SubscriptionServer.SubProtocol];
        });
        app.UseGraphQLGraphiQL("/api/graphql", new() {
            GraphQLEndPoint = "graphql",
            SubscriptionsEndPoint = "graphql",
            HeaderEditorEnabled = true,
            RequestCredentials = GraphQL.Server.Ui.GraphiQL.RequestCredentials.SameOrigin,
            GraphQLWsSubscriptions = true
        });
        app.UseEndpoints(endpoints => endpoints.MapControllers());
    }

    public async Task RunInitializationTestsAsync(IServiceProvider provider, CancellationToken ct = default)
    {
        provider.GetRequiredService<ISchema>().Initialize();
        provider.GetRequiredService<IEfGraphQLService<AppDbContext>>().GetPrimaryKeyNames<NetworkGroup>();
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
        await scope.ServiceProvider.GetRequiredService<AdminService>().BootstrapAsync(ct);
    }

    internal static void ConfigureAuthorizationOptions(AuthorizationOptions options)
        => options.AddPolicy(SecurityPolicies.ADMIN_POLICY, builder => builder.RequireAuthenticatedUser().RequireRole("Admin")
            .AddRequirements(new AdministratorAuthorizationHandler.Requirement()));
}
