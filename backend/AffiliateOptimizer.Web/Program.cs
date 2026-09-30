using System.Globalization;
using AffiliateOptimizer.Web;
using AffiliateOptimizer.Web.Infrastructure;
using AffiliateOptimizer.Web.Infrastructure.AffiliateService;

// Rationales, messages and logs are written for operators anywhere: never in the host machine's number format.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

// The original client read APP_KEY / APP_SECRET / SERVER_URL; keep honouring those names.
builder.Configuration.AddLegacyAffiliateServiceVariables();

// ArchitectureSimple.md §8.1 step 1, telemetry: OpenTelemetry logs, traces and metrics, exported when an Aspire dashboard
// is listening. Registered here, not in Startup, so the composition test keeps building Startup on a bare ServiceCollection.
builder.AddServiceDefaults();

var startup = new Startup(builder.Configuration, builder.Environment);
startup.ConfigureServices(builder.Services);

var app = builder.Build();
startup.Configure(app);
app.MapDefaultEndpoints(); // /health and /alive (Development only), next to the /status that Startup maps

// Migrate and seed before the hosted loops start (they start with the host, after this returns).
await DatabaseInitializer.InitializeAsync(app);

await app.RunAsync();
