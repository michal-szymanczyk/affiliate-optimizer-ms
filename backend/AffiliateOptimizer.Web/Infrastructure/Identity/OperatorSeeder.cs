using AffiliateOptimizer.Repository.Entities;
using AffiliateOptimizer.Web.Configuration;
using Microsoft.AspNetCore.Identity;

namespace AffiliateOptimizer.Web.Infrastructure.Identity;

/// <summary>Bound from "Operator": the single operator account seeded at start-up.</summary>
public sealed class OperatorSeedOptions
{
    public const string SectionName = "Operator";

    public string Email { get; set; } = "operator@local";

    /// <summary>Development carries a labelled default; elsewhere it must come from user secrets or the environment.</summary>
    public string Password { get; set; } = string.Empty;
}

public static class OperatorSeeder
{
    public static async Task EnsureOperatorAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var options = services.GetRequiredService<OperatorSeedOptions>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(OperatorSeeder));

        if (string.IsNullOrWhiteSpace(options.Email) || string.IsNullOrWhiteSpace(options.Password))
            throw new InvalidOperationException(
                "Operator:Email and Operator:Password must be set so the operator can sign in. " +
                "Set the password with `dotnet user-secrets set \"Operator:Password\" \"<value>\"` in backend/AffiliateOptimizer.Web " +
                "or the Operator__Password environment variable, then restart the host.");

        var roles = services.GetRequiredService<RoleManager<AppRole>>();
        var users = services.GetRequiredService<UserManager<AppUser>>();

        if (!await roles.RoleExistsAsync(Policies.OperatorRole))
            Ensure(await roles.CreateAsync(new AppRole(Policies.OperatorRole)), "create the Operator role");

        var user = await users.FindByEmailAsync(options.Email);
        if (user is null)
        {
            user = new AppUser { UserName = options.Email, Email = options.Email, EmailConfirmed = true };
            Ensure(await users.CreateAsync(user, options.Password), $"create the operator account {options.Email}");
            logger.LogInformation("Seeded the operator account {Email}", options.Email);
        }

        if (!await users.IsInRoleAsync(user, Policies.OperatorRole))
            Ensure(await users.AddToRoleAsync(user, Policies.OperatorRole), "assign the Operator role");

        ct.ThrowIfCancellationRequested();
    }

    private static void Ensure(IdentityResult result, string action)
    {
        if (result.Succeeded) return;
        var reasons = string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
        throw new InvalidOperationException($"Could not {action}: {reasons}");
    }
}
