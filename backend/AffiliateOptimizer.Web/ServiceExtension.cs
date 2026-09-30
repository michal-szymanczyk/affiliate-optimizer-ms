using AffiliateOptimizer.Domain.Common;
using AffiliateOptimizer.Domain.Common.AffiliateService;
using AffiliateOptimizer.Domain.Common.Loop;
using AffiliateOptimizer.Web.Infrastructure.AffiliateService;
using AffiliateOptimizer.Web.Infrastructure.Identity;
using AffiliateOptimizer.Web.Infrastructure.Loops;
using Common.Domain.Middleware;

namespace AffiliateOptimizer.Web;

public static class ServiceExtension
{
    /// <summary>Web implementations of Domain contracts (ArchitectureSimple.md §8.1 step 9).</summary>
    public static IServiceCollection AddWebServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IUserContext, UserContext>();
        services.AddSingleton<ICredentialContext, CredentialContext>();
        services.AddSingleton<ILoopHeartbeat, LoopHeartbeat>();
        services.AddSingleton<IExceptionResponseMapper, AffiliateServiceExceptionMapper>();

        // One attempt per call, no resilience handler: POST /clicks/next is not idempotent and the loop owns retry policy.
        services.AddHttpClient<IAffiliateServiceClient, AffiliateServiceClient>((provider, http) =>
        {
            var options = provider.GetRequiredService<AffiliateServiceOptions>();
            http.BaseAddress = new Uri(options.ServerUrl.TrimEnd('/') + "/");   // trailing slash keeps any path prefix
            http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            http.DefaultRequestHeaders.Add("X-App-Key", options.AppKey);
            http.DefaultRequestHeaders.Add("X-App-Secret", options.AppSecret);
        });

        // Hosted services start in registration order: the start-up run policy first, then the two loops.
        services.AddHostedService<LoopStartupService>();
        services.AddHostedService<ClickLoopService>();
        services.AddHostedService<CommissionPollLoopService>();

        return services;
    }
}
