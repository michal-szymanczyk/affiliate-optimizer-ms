using System.Text.Json;
using System.Text.Json.Serialization;
using AffiliateOptimizer.Domain;
using AffiliateOptimizer.Domain.Common;
using AffiliateOptimizer.Domain.Common.AffiliateService;
using AffiliateOptimizer.Domain.Common.Optimization.Models;
using AffiliateOptimizer.Repository;
using AffiliateOptimizer.Web.Configuration;
using AffiliateOptimizer.Web.Infrastructure;
using AffiliateOptimizer.Web.Infrastructure.AffiliateService;
using AffiliateOptimizer.Web.Infrastructure.FeaturesLocator;
using AffiliateOptimizer.Web.Infrastructure.Identity;
using Common.Domain.Middleware;
using Common.Domain.Requests;
using Common.Domain.Time;
using Common.Domain.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace AffiliateOptimizer.Web;

/// <summary>Composition root, in the order of ArchitectureSimple.md §8.1 (steps that do not apply are left out).</summary>
public sealed class Startup(IConfiguration configuration, IWebHostEnvironment environment)
{
    public void ConfigureServices(IServiceCollection services)
    {
        var domainAssembly = typeof(Domain.ServiceExtension).Assembly;
        var commonAssembly = typeof(IDispatcher).Assembly;

        // 1. typed options singletons (inject the options object, not IConfiguration)
        services.AddOptions<AffiliateServiceOptions>()
            .Bind(configuration.GetSection(AffiliateServiceOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AffiliateServiceOptions>, AffiliateServiceOptionsValidator>();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<AffiliateServiceOptions>>().Value);
        services.AddSingleton(Bind<EvaluationOptions>(EvaluationOptions.SectionName));
        services.AddSingleton(Bind<LoopOptions>(LoopOptions.SectionName));
        services.AddSingleton(Bind<OperatorSeedOptions>(OperatorSeedOptions.SectionName));
        services.AddSingleton(Bind<EpsilonGreedyOptions>("Models:epsilon-greedy"));
        services.AddSingleton(Bind<SlidingWindowUcbOptions>("Models:sliding-window-ucb"));
        services.Configure<ExceptionHandlingOptions>(o => o.HideExceptionDetailsFromClient = configuration.GetValue<bool>("HideExceptionDetailsFromClient"));

        // 2. authorization: named policies, deny by default (step 4)
        services.AddAuthorization(Policies.Configure);

        // 3. routing
        services.AddRouting(o => o.LowercaseUrls = true);

        // 4. MVC: authenticated by default, feature folders, camelCase JSON with enums as strings
        services.AddControllersWithViews(options =>
            {
                options.Filters.Add(new AuthorizeFilter(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()));
                options.Conventions.Add(new FeatureConvention());
            })
            .AddRazorOptions(RazorOptions.AddFeaturesLocator)
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

        // 7. request pipeline
        services.AddRequestDispatching(domainAssembly, commonAssembly);
        services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
        services.AddClosedImplementations(typeof(IValidatorBase<>), [domainAssembly]);

        // 8. persistence + Identity stores
        var connectionString = configuration.GetConnectionString("App")
                               ?? throw new InvalidOperationException("ConnectionStrings:App is not configured.");
        services.AddRepository(connectionString);

        // 9. web services: user context, credential context, typed HTTP client, hosted loops
        services.AddWebServices();

        // 12. domain: models, catalog, random source, list queries
        services.AddDomain();

        // 14. time
        services.AddSystemTime();

        // 16. Identity + application cookie
        services.Configure<IdentityOptions>(o =>
        {
            o.User.RequireUniqueEmail = true;
            o.SignIn.RequireConfirmedAccount = false;
            if (environment.IsDevelopment())
            {
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireDigit = false;
                o.Password.RequiredLength = 6;
            }
        });
        services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = "AffiliateOptimizer.Operator";
            o.LoginPath = "/authentication/login";
            o.LogoutPath = "/authentication/logout";
            o.ExpireTimeSpan = TimeSpan.FromHours(12);
            o.SlidingExpiration = true;
            // API calls get status codes, not HTML redirects; the SPA turns a 401 into a login redirect itself.
            o.Events.OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
            o.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        // 18. health checks at /status
        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("sql")
            .AddCheck<AffiliateServiceHealthCheck>("affiliate-service");
    }

    public void Configure(WebApplication app)
    {
        app.UseMiddleware<ExceptionHandlerMiddleware>();
        if (!environment.IsDevelopment()) app.UseHsts();

        app.UseAuthentication();

        var spa = ResolveSpaRoot();
        if (spa is not null)
        {
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = spa,
                OnPrepareResponse = context =>
                {
                    if (environment.IsDevelopment()) context.Context.Response.Headers.CacheControl = "no-cache";
                },
            });
        }

        app.UseRouting();
        app.UseAuthorization();

        app.MapControllers();
        app.MapHealthChecks("/status", new HealthCheckOptions { ResponseWriter = HealthStatusWriter.Write }).AllowAnonymous();
        app.Map("/api/{**rest}", () => Results.NotFound(new { message = "Unknown API route." }));

        if (spa is not null)
        {
            app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = spa })
                .RequireAuthorization(Policies.Operators);
        }
        else
        {
            app.MapGet("/", () => Results.Text(
                "Affiliate Optimizer API is running. No operator UI build was found: run `npm run build` in frontend/ " +
                "or start the Vite dev server on http://localhost:5174. Health: /status, API: /api/...")).AllowAnonymous();
        }
    }

    private T Bind<T>(string section) where T : class, new()
        => configuration.GetSection(section).Get<T>() ?? new T();

    /// <summary>Spa:RootPath relative to the content root; null when no build exists (the API still runs).</summary>
    private IFileProvider? ResolveSpaRoot()
    {
        var rootPath = configuration["Spa:RootPath"] ?? "wwwroot";
        var fullPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, rootPath));
        return File.Exists(Path.Combine(fullPath, "index.html")) ? new PhysicalFileProvider(fullPath) : null;
    }
}
