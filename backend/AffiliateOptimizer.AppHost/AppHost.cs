using Microsoft.Extensions.Configuration;

// Development orchestration. ArchitectureSimple.md §11 describes a Dockerfile and deploy.yaml for deployment; this
// project only runs things locally and is never published. Resource names are the service names in the dashboard.
var builder = DistributedApplication.CreateBuilder(args);

// ---- Database -------------------------------------------------------------------------------------------------
// LocalDB, from ConnectionStrings:App in this project's appsettings.json (the same string the host carries itself).
var db = builder.AddConnectionString("App");

// ---- The host -------------------------------------------------------------------------------------------------
// Public URL stays http://localhost:5175 (the launch profile, proxied); Kestrel itself listens where ASPNETCORE_URLS says.
// /status is the readiness probe the runbook documents: 503 until the Affiliate Service answers, so the dashboard shows
// the host Unhealthy while the service is down.
var web = builder.AddProject<Projects.AffiliateOptimizer_Web>("web")
    .WithReference(db)
    .WithHttpHealthCheck("/status");

// ---- The Affiliate Service: the local simulator (Development default) or real credentials -----------------------
// Switch: AffiliateService:UseSimulator (appsettings.Development.json = true; user secrets or the environment variable
// AffiliateService__UseSimulator=false for the parameters below). Exactly one host per credential pair: never WithReplicas.
if (builder.Configuration.GetValue<bool>("AffiliateService:UseSimulator"))
{
    const string simulatorAppKey = "ak_local";
    const string simulatorAppSecret = "as_local";

    var simulator = builder.AddNodeApp("affiliate-simulator", appDirectory: "../../tools/affiliate-simulator", scriptPath: "server.mjs")
        .WithHttpEndpoint(port: 8787, env: "PORT")
        .WithHttpHealthCheck("/_simulator/truth")
        .WithEnvironment("APP_KEY", simulatorAppKey)
        .WithEnvironment("APP_SECRET", simulatorAppSecret)
        .WithEnvironment("DISCLOSURE_DELAY_MS", "8000");

    web.WithEnvironment("AffiliateService__ServerUrl", simulator.GetEndpoint("http"))
        .WithEnvironment("AffiliateService__AppKey", simulatorAppKey)
        .WithEnvironment("AffiliateService__AppSecret", simulatorAppSecret)
        .WaitFor(simulator);
}
else
{
    // Values come from this project's user secrets (Parameters:affiliate-*) or from the dashboard's prompt; never from the repo.
    var serverUrl = builder.AddParameter("affiliate-server-url");
    var appKey = builder.AddParameter("affiliate-app-key");
    var appSecret = builder.AddParameter("affiliate-app-secret", secret: true);

    web.WithEnvironment("AffiliateService__ServerUrl", serverUrl)
        .WithEnvironment("AffiliateService__AppKey", appKey)
        .WithEnvironment("AffiliateService__AppSecret", appSecret);
}

// ---- The operator UI ------------------------------------------------------------------------------------------
// Vite on http://localhost:5174, proxying /api, /authentication and /status to the host (vite.config.ts reads
// VITE_API_PROXY_TARGET). AddViteApp already owns the "http" endpoint (PORT + --port), so only its public port is pinned.
// Deliberately not WaitFor(web): the UI shows "host unreachable" itself, and /status stays 503 while the service is down.
builder.AddViteApp("frontend", "../../frontend")
    .WithNpm(install: true)
    .WithEndpoint("http", endpoint => endpoint.Port = 5174)
    .WithEnvironment("VITE_API_PROXY_TARGET", web.GetEndpoint("http"));

builder.Build().Run();
