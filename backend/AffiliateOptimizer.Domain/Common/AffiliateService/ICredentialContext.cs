namespace AffiliateOptimizer.Domain.Common.AffiliateService;

/// <summary>Which credential pair this host routes with, as a fingerprint. The secret itself never enters the Domain.</summary>
public interface ICredentialContext
{
    string Fingerprint { get; }
}
