using AppGraphQL;
using AppServices;
using GraphQL.AspNetCore3;
using GraphQL.Types;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.IdentityModel.Tokens;

namespace AppServer;

public sealed class Startup(IConfiguration configuration)
{
    public void ConfigureServices(IServiceCollection services)
    {
        var entra = EntraConfiguration.Read(configuration);
        services.AddSingleton(entra);
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => {
            options.MapInboundClaims = false;
            if (entra.Enabled) {
                options.Authority = entra.Authority;
                options.Audience = entra.ClientId;
            }
            options.TokenValidationParameters = new TokenValidationParameters {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                RequireSignedTokens = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = entra.Authority,
                ValidAudience = entra.ClientId,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                NameClaimType = "name",
                RoleClaimType = System.Security.Claims.ClaimTypes.Role,
                ClockSkew = TimeSpan.FromMinutes(1)
            };
            options.Events.OnTokenValidated = async context => {
                if (context.Principal?.Identity is not System.Security.Claims.ClaimsIdentity identity ||
                    !await context.HttpContext.RequestServices.GetRequiredService<EntraIdentityService>().ApplyAsync(identity, context.HttpContext.RequestAborted))
                    context.Fail("A delegated Entra access token for the configured tenant is required.");
            };
        });
        services.AddAuthorization(ConfigureAuthorizationOptions);
        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(configuration.GetConnectionString(nameof(AppDbContext))));
        services.AddHttpContextAccessor();
        services.AddSingleton(configuration);
        services.AddAutoMapper((provider, config) => config.AddProfile(provider.GetRequiredService<AppGraphQL.AutoMapper.AutoMapperProfile>()), typeof(Program).Assembly);
        services.AddRouting();
        services.AddControllers();
        services.AddHttpsRedirection(options => options.HttpsPort = 443);
        services.AddResponseCompression(options => {
            options.EnableForHttps = true;
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Append("application/graphql-response+json");
        });
        AppServices.Startup.ConfigureServices(services);
        AppGraphQL.Startup.ConfigureGraphQL(services, configuration);
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsProduction())
            app.UseHttpsRedirection();
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
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseGraphQL("/api/graphql", options => {
            options.AuthorizationRequired = EntraConfiguration.Read(configuration).RequireSignIn;
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
        await scope.ServiceProvider.GetRequiredService<AdminService>().BootstrapAsync(ct);
    }

    internal static void ConfigureAuthorizationOptions(Microsoft.AspNetCore.Authorization.AuthorizationOptions options)
        => options.AddPolicy(SecurityPolicies.ADMIN_POLICY, builder => builder.RequireRole("Admin"));
}
