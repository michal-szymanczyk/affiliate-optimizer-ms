using Microsoft.AspNetCore.Authorization;

namespace AffiliateOptimizer.Web.Configuration;

/// <summary>Named policies (ArchitectureSimple.md §8.2). One role for now: the operator responsible for the routing system.</summary>
public static class Policies
{
    public const string Operators = "Operators";
    public const string OperatorRole = "Operator";

    public static void Configure(AuthorizationOptions options)
        => options.AddPolicy(Operators, policy => policy.RequireAuthenticatedUser().RequireRole(OperatorRole));
}
