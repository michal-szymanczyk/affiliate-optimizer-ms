using AffiliateOptimizer.Repository;
using AffiliateOptimizer.Web.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace AffiliateOptimizer.Web.Infrastructure;

public static class DatabaseInitializer
{
    /// <summary>Applies pending migrations (Database:MigrateOnStartup, default true) and seeds the operator account.</summary>
    public static async Task InitializeAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DatabaseInitializer));

        if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
        {
            var context = services.GetRequiredService<AppDbContext>();
            var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();
            if (pending.Count > 0) logger.LogInformation("Applying {Count} pending migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
            await context.Database.MigrateAsync();
        }

        await OperatorSeeder.EnsureOperatorAsync(services);
    }
}
