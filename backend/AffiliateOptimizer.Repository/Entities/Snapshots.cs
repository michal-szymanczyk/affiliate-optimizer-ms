using System.Text.Json;
using AffiliateOptimizer.Domain.Common.AffiliateService;
using Microsoft.AspNetCore.Identity;

namespace AffiliateOptimizer.Repository.Entities;

/// <summary>The network catalogue as the run saw it at start: advertised rates and coverage are provenance, not live data.</summary>
public class RunNetworkSnapshot
{
    public int RunId { get; private set; }
    public string NetworkId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public decimal AdvertisedRate { get; private set; }
    public string SupportedMerchantIdsJson { get; private set; } = "[]";
    public int CoverageCount { get; private set; }

    private RunNetworkSnapshot() { }

    public static RunNetworkSnapshot From(Network network) => new()
    {
        NetworkId = network.Id,
        Name = network.Name,
        AdvertisedRate = network.AdvertisedRate,
        SupportedMerchantIdsJson = JsonSerializer.Serialize(network.SupportedMerchantIds),
        CoverageCount = network.SupportedMerchantIds.Count,
    };

    public Network ToNetwork() => new(NetworkId, Name, AdvertisedRate, JsonSerializer.Deserialize<List<int>>(SupportedMerchantIdsJson) ?? []);
}

public class RunMerchantSnapshot
{
    public int RunId { get; private set; }
    public int MerchantId { get; private set; }
    public string Name { get; private set; } = string.Empty;

    private RunMerchantSnapshot() { }

    public static RunMerchantSnapshot From(Merchant merchant) => new() { MerchantId = merchant.MerchantId, Name = merchant.Name };
}

/// <summary>The operator account (ASP.NET Core Identity, integer keys).</summary>
public class AppUser : IdentityUser<int>
{
}

public class AppRole : IdentityRole<int>
{
    public AppRole() { }

    public AppRole(string name) : base(name) { }
}
