using AffiliateOptimizer.Domain.Common.AffiliateService;
using AffiliateOptimizer.Domain.Common.Loop;
using Common.Domain.Middleware;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace AffiliateOptimizer.Web.Infrastructure.AffiliateService;

/// <summary>Fails loudly at start-up with a message that says where to put the value (the original client did the same).</summary>
public sealed class AffiliateServiceOptionsValidator : IValidateOptions<AffiliateServiceOptions>
{
    public ValidateOptionsResult Validate(string? name, AffiliateServiceOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ServerUrl))
            failures.Add(Missing("ServerUrl", "SERVER_URL", "the Affiliate Service base URL"));
        else if (!Uri.TryCreate(options.ServerUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            failures.Add($"AffiliateService:ServerUrl \"{options.ServerUrl}\" is not an absolute http(s) URL.");

        if (string.IsNullOrWhiteSpace(options.AppKey)) failures.Add(Missing("AppKey", "APP_KEY", "the public half of your credential pair"));
        if (string.IsNullOrWhiteSpace(options.AppSecret)) failures.Add(Missing("AppSecret", "APP_SECRET", "the private half of your credential pair"));
        if (options.TimeoutSeconds is < 1 or > 120) failures.Add("AffiliateService:TimeoutSeconds must be between 1 and 120.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static string Missing(string key, string legacyVariable, string what)
        => $"AffiliateService:{key} is not set ({what}). Set it with `dotnet user-secrets set \"AffiliateService:{key}\" \"<value>\"` " +
           $"in backend/AffiliateOptimizer.Web, or export the {legacyVariable} environment variable, then restart the host.";
}

/// <summary>Maps the legacy APP_KEY / APP_SECRET / SERVER_URL variables of the original client onto the AffiliateService section.</summary>
public static class LegacyAffiliateServiceVariables
{
    public static IConfigurationBuilder AddLegacyAffiliateServiceVariables(this IConfigurationBuilder builder)
    {
        var values = new Dictionary<string, string?>();
        Map("APP_KEY", $"{AffiliateServiceOptions.SectionName}:{nameof(AffiliateServiceOptions.AppKey)}");
        Map("APP_SECRET", $"{AffiliateServiceOptions.SectionName}:{nameof(AffiliateServiceOptions.AppSecret)}");
        Map("SERVER_URL", $"{AffiliateServiceOptions.SectionName}:{nameof(AffiliateServiceOptions.ServerUrl)}");

        if (values.Count > 0) builder.AddInMemoryCollection(values);
        return builder;

        void Map(string variable, string key)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value)) values[key] = value.Trim();
        }
    }
}

/// <summary>The Domain sees only a fingerprint of the credential pair.</summary>
public sealed class CredentialContext(AffiliateServiceOptions options) : ICredentialContext
{
    public string Fingerprint { get; } = CredentialFingerprint.Compute(options.AppKey);
}

/// <summary>Upstream failures get their own statuses so "the client cannot start" carries the server's own code.</summary>
public sealed class AffiliateServiceExceptionMapper : IExceptionResponseMapper
{
    public bool TryMap(Exception exception, out int statusCode, out object body)
    {
        switch (exception)
        {
            case AffiliateServiceException upstream:
                statusCode = StatusCodes.Status502BadGateway;
                body = new
                {
                    message = $"The Affiliate Service answered {upstream.Status} {upstream.Code}: {upstream.Message}",
                    code = upstream.Code,
                    upstreamStatus = upstream.Status,
                    retryAfterSeconds = upstream.RetryAfterSeconds,
                };
                return true;
            case AffiliateServiceTransportException transport:
                statusCode = StatusCodes.Status503ServiceUnavailable;
                body = new { message = transport.Message, code = LoopEventCodes.Transport };
                return true;
            default:
                statusCode = 0;
                body = null!;
                return false;
        }
    }
}

/// <summary>Reachability and credentials, checked with the cheapest un-throttled call there is.</summary>
public sealed class AffiliateServiceHealthCheck(IAffiliateServiceClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var networks = await client.GetNetworks(cancellationToken);
            return HealthCheckResult.Healthy($"{networks.Count} networks published.");
        }
        catch (AffiliateServiceException exception) when (exception.Code == AffiliateServiceErrorCodes.Unauthorized)
        {
            return HealthCheckResult.Degraded("The Affiliate Service rejected the credentials (UNAUTHORIZED).", exception);
        }
        catch (AffiliateServiceException exception)
        {
            return HealthCheckResult.Degraded($"The Affiliate Service answered {exception.Status} {exception.Code}.", exception);
        }
        catch (AffiliateServiceTransportException exception)
        {
            return HealthCheckResult.Unhealthy(exception.Message, exception);
        }
    }
}
