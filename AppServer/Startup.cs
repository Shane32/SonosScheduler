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

public class Startup
{
    public IConfiguration Configuration { get; }

    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    private static readonly Action<ILogger, Exception?> _migratingDatabase = LoggerMessage.Define(
        LogLevel.Information,
        new EventId(1, nameof(RunInitializationTestsAsync)),
        "Migrating database");

    private static readonly Action<ILogger, Exception?> _databaseMigrated = LoggerMessage.Define(
        LogLevel.Information,
        new EventId(2, nameof(RunInitializationTestsAsync)),
        "Database migrated");

    private static readonly Action<ILogger, Exception?> _databaseMigrationError = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(3, nameof(RunInitializationTestsAsync)),
        "An error occurred while migrating the database");

    public void ConfigureServices(IServiceCollection services)
    {
        // configure authentication
        services.AddAuthentication(Configuration["Authentication:DefaultScheme"] ?? JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options => {
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = "name";
                options.Events = new() {
                    OnTokenValidated = async context => {
                        var dbAuthService = context.HttpContext.RequestServices.GetRequiredService<DbAuthService>();
                        await dbAuthService.OnTokenValidatedAsync(context, context.HttpContext.RequestAborted);
                    }
                };
            });

        // register DbAuthService
        services.AddSingleton<DbAuthService>();

        // configure authorization
        services.AddAuthorization(ConfigureAuthorizationOptions);

        // configure cors
        var origins = Configuration.GetSection("Cors").GetValue<string>("AllowedOrigins")?.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        services.AddCors(o => {
            o.AddDefaultPolicy(builder => {
                builder.WithOrigins(origins)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials();
            });
        });

        // configure database
        services.AddDbContext<AppDbContext>(options => {
#if DEBUG
            options.LogTo(x => Debug.WriteLine(x), LogLevel.Information);
#endif
            options.UseSqlServer(
                Configuration.GetConnectionString(nameof(AppDbContext)),
                options => {
                    if (AppDbContext.Schema != null)
                        options.MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.Schema);
                });
        });

        // configure Server services
        services.AddHttpContextAccessor();
        services.AddSingleton(Configuration);
        services.AddAutoMapper((serviceProvider, config) => {
            config.AddCollectionMappers();
            config.UseEntityFrameworkCoreModel<AppDbContext>(serviceProvider);
            config.ConstructServicesUsing(serviceProvider.GetRequiredService);
            config.AddProfile(serviceProvider.GetRequiredService<AppGraphQL.AutoMapper.AutoMapperProfile>());
        }, typeof(Program).Assembly);
        services.AddRouting();
        services.AddControllers();
        services.AddSingleton<TokenCredential>(serviceProvider => {
            var clientId = serviceProvider.GetRequiredService<IConfiguration>()["AZURE_CLIENT_ID"] ?? "";
            if (string.IsNullOrWhiteSpace(clientId))
                return new DefaultAzureCredential();
            if (!Guid.TryParse(clientId, out _))
                throw new InvalidOperationException("AZURE_CLIENT_ID must be the runtime user-assigned managed identity's client ID.");
            return new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId));
        });
        services.AddHttpClient("MicrosoftOAuth", client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        services.AddResponseCompression(options => {
            options.EnableForHttps = true;
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Append("application/graphql-response+json");
        });

        // configure GraphQL services
        AppGraphQL.Startup.ConfigureGraphQL(services, Configuration);

        // configure other services
        AppServices.Startup.ConfigureServices(services, Configuration);
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.UseResponseCompression();

        // Configure the HTTP request pipeline.
        if (!env.IsDevelopment()) {
            //app.UseExceptionHandler("/Home/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseHttpsRedirection();

        // Serve static files from /static with cache control headers
        if (!string.IsNullOrEmpty(env.WebRootPath)) {
            var staticPath = Path.Combine(env.WebRootPath, "static");
            if (Directory.Exists(staticPath)) {
                app.UseCompressedStaticFiles(new() {
                    FileSystemPath = Path.Combine(env.WebRootPath, "static"),
                    RequestPath = "/static",
                    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable",
                });
            }
        }

        // Serve the SPA for all paths that don't start with /api or /static
        List<PathString> apiPathStrings = ["/api", "/static"];
        app.MapWhen(context => !apiPathStrings.Any(apiPathString => context.Request.Path.StartsWithSegments(apiPathString, StringComparison.OrdinalIgnoreCase)), app => {
            // Serve other static files without caching (verify etag on each load)
            app.UseStaticFiles(new StaticFileOptions() {
                OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
            });
            // Serve index.html for unmatched files without caching (verify etag on each load)
            app.UseSpa(spa => {
                spa.Options.SourcePath = env.WebRootPath;
                spa.Options.DefaultPage = "/index.html";
                spa.Options.DefaultPageStaticFileOptions = new() {
                    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
                };
#if DEBUG
                spa.UseProxyToSpaDevelopmentServer("http://localhost:5173");
#endif
            });
        });

        app.UseCors();
        app.UseWebSockets();
        // CORS does not protect WebSockets, so validate browser origins to prevent unrelated websites
        // from using a LAN visitor's browser to access the anonymous GraphQL controls.
        var allowedOrigins = Configuration.GetSection("Cors").GetValue<string>("AllowedOrigins")?.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries) ?? [];
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

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseGraphQL("/api/graphql", o => {
            // LAN controls are public unless sign-in is enabled; administrative fields have their own policy.
            o.AuthorizationRequired = Configuration.GetValue<bool>("Sonos:RequireSignIn");
            // we will use the same endpoint for GraphiQL so disable GET requests
            o.HandleGet = false;
            // do not allow batched requests
            o.EnableBatchedRequests = false;
            // allow reading the document id from the query string for better logging of persisted queries
            o.ReadQueryStringOnPost = true;
            // let the graphql client process errors by examining the response
            o.ValidationErrorsReturnBadRequest = false;
            // Public LAN controls must reject cross-site requests from unrelated browser pages.
            o.CsrfProtectionEnabled = true;
            o.ReadFormOnPost = false;

            // limit WebSocket requests to the newer protocol with secure keep-alive mechanics
            o.WebSockets.KeepAliveMode = GraphQL.AspNetCore3.WebSockets.KeepAliveMode.TimeoutWithPayload;
            o.WebSockets.KeepAliveTimeout = TimeSpan.FromSeconds(10);
            o.WebSockets.SupportedWebSocketSubProtocols = [GraphQL.AspNetCore3.WebSockets.GraphQLWs.SubscriptionServer.SubProtocol];
        });

        app.UseGraphQLGraphiQL("/api/graphql", new() {
            GraphQLEndPoint = "graphql",
            SubscriptionsEndPoint = "graphql",
            HeaderEditorEnabled = true,
            RequestCredentials = GraphQL.Server.Ui.GraphiQL.RequestCredentials.SameOrigin,
            GraphQLWsSubscriptions = true,
        });

        app.UseEndpoints(endpoints => {
            endpoints.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}");
        });
    }

    public async Task RunInitializationTestsAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        // Initialize GraphQL
        serviceProvider.GetRequiredService<ISchema>().Initialize();
        // Ensure db model can be read
        serviceProvider.GetRequiredService<IEfGraphQLService<AppDbContext>>().GetPrimaryKeyNames<NetworkGroup>(); // any table
        // Auto migrate the database
        await using var scope = serviceProvider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILogger<Startup>>();
        try {
            var databaseStartupService = services.GetRequiredService<DatabaseStartupService>();
            if (await databaseStartupService.HasPendingMigrationsAsync(cancellationToken)) {
                _migratingDatabase(logger, null);
                await databaseStartupService.MigrateAsync(cancellationToken);
                _databaseMigrated(logger, null);
            }
        } catch (Exception ex) {
            _databaseMigrationError(logger, ex);
            throw;
        }

        var context = services.GetRequiredService<AppDbContext>();
        var interrupted = await context.ScheduleRuns.Where(x => x.Status == ScheduleRunStatus.Running).ToListAsync(cancellationToken);
        foreach (var run in interrupted) {
            run.Status = ScheduleRunStatus.Interrupted;
            run.CompletedAt = services.GetRequiredService<TimeProvider>().GetUtcNow();
            run.Message = "Application restarted after this occurrence was claimed; it will not be retried.";
            services.GetRequiredService<AuditService>().Add("Schedule", run.Message, run.ScheduleId, "Warning");
        }
        await context.SaveChangesAsync(cancellationToken);
        await services.GetRequiredService<AdminService>().BootstrapAsync(cancellationToken);
    }

    internal static void ConfigureAuthorizationOptions(AuthorizationOptions options)
    {
        options.AddPolicy(SecurityPolicies.ADMIN_POLICY, builder => builder.RequireAuthenticatedUser().RequireRole("Admin")
            .AddRequirements(new AdministratorAuthorizationHandler.Requirement()));
    }
}
