# Affiliate Optimizer — .NET host

The routing loop, the optimization models, the observation store and the operator API live here. The React
operator UI in [`../frontend`](../frontend) talks only to this host; the Affiliate Service credentials never
reach a browser.

The solution follows [`../ArchitectureSimple.md`](../ArchitectureSimple.md): feature slices under
`AffiliateOptimizer.Domain/Features/<Area>/<UseCase>/`, an in-house request dispatcher in `Common.Domain`,
explicit record mapping, one repository class per area in `AffiliateOptimizer.Repository`, thin controllers in
`AffiliateOptimizer.Web`. [`../WALKTHROUGH.md`](../WALKTHROUGH.md) explains the design and the deviations.

## Prerequisites

| Need | Version |
| --- | --- |
| .NET SDK | 10.0.100 or newer (`global.json` rolls forward to the newest 10.0 feature band) |
| SQL Server | LocalDB (`(localdb)\MSSQLLocalDB`, ships with Visual Studio / SQL Server Express) **or** `docker compose up -d` in this folder |
| Node.js | 22.13+ only for the operator UI and the simulator (see the root README) |
| Aspire | nothing to install: `dotnet run` on the AppHost acquires the Aspire CLI bundle through `dnx` on first use |

## Credentials and secrets (never committed)

Three values are issued out of band. Give them to the host either as user secrets:

```bash
cd backend/AffiliateOptimizer.Web
dotnet user-secrets set "AffiliateService:ServerUrl" "https://..."
dotnet user-secrets set "AffiliateService:AppKey" "ak_..."
dotnet user-secrets set "AffiliateService:AppSecret" "as_..."
```

or as the environment variables the original client used, `SERVER_URL`, `APP_KEY`, `APP_SECRET` (also accepted:
`AffiliateService__ServerUrl` and friends). A missing value stops the host at start-up with a message that says
where to put it.

The operator account is seeded at start-up from `Operator:Email` / `Operator:Password`. In the Development
environment the password defaults to `operator-dev-password` (`appsettings.Development.json`, labelled as such);
in any other environment set `Operator:Password` the same way as the secrets above.

## Run

```bash
cd backend
dotnet run --project AffiliateOptimizer.Web
```

On start the host applies pending migrations, seeds the operator, starts the two loops and listens on
`http://localhost:5175`. Sign in at `/authentication/login`. With the UI built (`npm run build` in `frontend/`)
the host serves it at `/`; during UI development run `npm run dev` in `frontend/` instead and open
`http://localhost:5174`, which proxies API calls here.

### Everything at once

```bash
cd backend
dotnet run --project AffiliateOptimizer.AppHost
```

The AppHost starts `tools/affiliate-simulator` (or uses real credentials from its own user secrets, see the root
README), this host on `http://localhost:5175` and the Vite server on `http://localhost:5174`, and shows logs,
traces, metrics and health at `http://localhost:15000`. Under the AppHost open 5174, not 5175: in Development the
host serves any stale `../../frontend/dist` it finds. The AppHost's database is `ConnectionStrings:App` in
`AffiliateOptimizer.AppHost/appsettings.json` (LocalDB, the same string as here).

| URL | What |
| --- | --- |
| `/status` | Health checks (SQL Server, Affiliate Service reachability, self), anonymous JSON |
| `/health`, `/alive` | Development only: every check / liveness only, plain text (Aspire service defaults) |
| `/api/runs/*` | start / pause / resume / stop / getcurrent / getall |
| `/api/clicks/getall`, `/api/clicks/getcounters` | the click feed (paged, sorted, filtered) and whole-run totals |
| `/api/networks/getinsight` | advertised vs realized CPC per network, with thin / stale / changing flags |
| `/api/evaluation/compare` | per-model arms of a run, with a verdict and its caveats |
| `/api/health/getstatus` | loop health, fault localisation, open loop events |
| `/api/models/getall` | the optimization models this host can run |
| `/api/loop-events/dismiss` | dismiss a dismissible event |

Every API response carries `serverNow`; the UI never uses the browser clock for derived state.

## Tests

```bash
cd backend
dotnet test
```

Handler, validator, entity, repository (EF in-memory), HTTP-client, controller-wrapping, hosted-loop and
composition tests; the last one proves every request type resolves exactly one handler from the real `Startup`.

## Database

```bash
dotnet ef migrations add <Name> -p AffiliateOptimizer.Repository -s AffiliateOptimizer.Web --context AppDbContext
dotnet ef database update -p AffiliateOptimizer.Repository -s AffiliateOptimizer.Web --context AppDbContext
```

`Database:MigrateOnStartup` (default `true`) applies migrations when the host starts. For a production
deployment generate an idempotent script instead (`dotnet ef migrations script --idempotent`).

Without LocalDB: `docker compose up -d` here, then override the connection string, e.g.
`ConnectionStrings__App="Server=localhost,1433;Database=AffiliateOptimizer;User Id=sa;Password=Affiliate!Optimizer1;TrustServerCertificate=True"`.

## Configuration keys (`appsettings.json`)

| Key | Meaning |
| --- | --- |
| `AffiliateService:*` | server URL, credential pair, per-call timeout (default 10 s) |
| `Operator:*` | the seeded operator account |
| `Loop:ResumeActiveRunOnStartup` | continue a run that was active when the host last stopped (default `true`); `false` stops it with an event |
| `Loop:AutoStartModel` | start a run with this model at boot when none is active (unattended operation) |
| `Models:<name>:*` | model parameters (`epsilon-greedy`: `Epsilon`, `MinSamples`; `sliding-window-ucb`: `C`) |
| `Evaluation:*` | recent window (10 min), stale-after (5 min), thin-below (10), min samples per arm (30) |
| `Spa:RootPath` | where the built UI lives (`wwwroot` after publish; `../../frontend/dist` in Development) |
| `HideExceptionDetailsFromClient` | `true` in production |

## Behaviour worth knowing

- Exactly one click chain exists per host: the click loop reads the run's status from the database every turn.
  Pause stops issuing clicks; polling never stops, so routed clicks still settle.
- The feed cursor is stored after every poll, including empty ones, so a restart never replays history. If the
  database is wiped, the feed replays the credential pair's whole history: those rows land in
  `OrphanDisclosures` and are counted on the Counters and Health screens rather than silently skipped.
- A route whose response was lost is retried identically up to three times (the server replays the outcome);
  after that the click is marked unconfirmed and the feed repairs it if the server accepted it.
- Run one host per credential pair. Two hosts would each issue clicks and share the same feed cursor row.
- Telemetry: `AffiliateOptimizer.ServiceDefaults` registers OpenTelemetry logs, traces and metrics from
  `Program.cs`; they are exported only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (the AppHost sets it), so a
  standalone run exports nothing. Health polling (`/status`, `/health`, `/alive`) is excluded from traces. The
  Affiliate client keeps exactly one attempt per call: the Aspire standard resilience handler is left out on purpose.

## Publish

```bash
cd backend
dotnet publish AffiliateOptimizer.Web -c Release -o ../publish
```

Publishing runs `npm ci && npm run build` in `../frontend` and ships `dist/` as `wwwroot/`
(`-p:SkipSpaBuild=true` publishes the API alone).
