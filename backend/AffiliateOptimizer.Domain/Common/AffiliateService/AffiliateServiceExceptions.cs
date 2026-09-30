namespace AffiliateOptimizer.Domain.Common.AffiliateService;

/// <summary>The server's stable error codes (API.md §Errors) plus the two the client itself produces.</summary>
public static class AffiliateServiceErrorCodes
{
    public const string MerchantNotSupported = "MERCHANT_NOT_SUPPORTED";
    public const string ClickExpired = "CLICK_EXPIRED";
    public const string ClickNotFound = "CLICK_NOT_FOUND";
    public const string NetworkNotFound = "NETWORK_NOT_FOUND";
    public const string InvalidCursor = "INVALID_CURSOR";
    public const string InvalidRequest = "INVALID_REQUEST";
    public const string RateLimited = "RATE_LIMITED";
    public const string Unauthorized = "UNAUTHORIZED";

    /// <summary>A response that was not JSON (a gateway page, for example). Produced by the client.</summary>
    public const string UnreadableResponse = "UNREADABLE_RESPONSE";

    /// <summary>A non-2xx response without a recognisable body. Produced by the client.</summary>
    public const string Unknown = "UNKNOWN";

    /// <summary>Only these responses mean the server committed a route that spent the click.</summary>
    public static bool IsRoutingRejection(string code) => code is MerchantNotSupported or ClickExpired;
}

/// <summary>The server answered with an error. Carries the HTTP status, the stable code and, when throttled, how long to wait.</summary>
public class AffiliateServiceException(int status, string code, string message, double? retryAfterSeconds = null) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public double? RetryAfterSeconds { get; } = retryAfterSeconds;

    /// <summary>True when the server committed a route that spent the click (422 or 410 on the route endpoint).</summary>
    public bool IsRoutingRejection => AffiliateServiceErrorCodes.IsRoutingRejection(Code);
}

/// <summary>
/// The request may or may not have reached the server: a connection failure, DNS, TLS or a timeout.
/// Repeating an identical route request after this is safe; repeating a click issue is not.
/// </summary>
public sealed class AffiliateServiceTransportException(string message, Exception? innerException = null) : Exception(message, innerException);
