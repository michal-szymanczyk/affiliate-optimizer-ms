# Affiliate Optimizer

Every three seconds, a click arrives for a merchant. Several affiliate networks may accept it.
Each advertises a cost per click (CPC), but the amount it actually pays is delayed, noisy, and
changes over time. For each click, you choose one network and later see only what that network paid.
Clicks are rationed, so learning from one choice means giving up the others.

The supplied client closed that loop by choosing randomly, in a browser tab. This repository turns it into
the beginnings of an optimization platform: a .NET 10 host runs the loop, keeps every observation, offers
four routing models behind one interface, compares them honestly, and explains itself to an operator through
the React UI. [`WALKTHROUGH.md`](WALKTHROUGH.md) is the walkthrough of the thinking.

## Start here

| Document | What it gives you |
| --- | --- |
| [`CONCEPTS`](CONCEPTS.md) | **The problem space.** What affiliate routing is, what each concept means, and the three properties that make the decision hard. |
| [`BRIEF`](BRIEF.md) | **The ask.** What we are looking for, the shape of the challenge, and what to hand back. |
| [`API`](API.md) | **The reference.** Five endpoints, the error codes, the rate limits, and a link to the complete machine-readable contract. |
| [`WALKTHROUGH`](WALKTHROUGH.md) | **What was built and why.** Direction, models, evaluation design, operator experience, deviations, what was left out. |
| [`ArchitectureSimple`](ArchitectureSimple.md) | **The architecture template** the .NET solution follows. |
| [`backend/README`](backend/README.md) | **The host runbook.** Secrets, database, endpoints, tests, migrations, publish. |

## Getting it running

Prerequisites: .NET SDK 10.0, SQL Server LocalDB (or Docker, see the backend runbook), Node.js 22.13+ and npm 10+.

### Everything at once: Aspire

`backend/AffiliateOptimizer.AppHost` starts the simulator, the host and the operator UI together and opens a
dashboard with their logs, traces, metrics and health:

```bash
cd backend
dotnet run --project AffiliateOptimizer.AppHost
```

The console prints the dashboard link (http://localhost:15000 with a one-time login token). The UI is at
http://localhost:5174, the host at http://localhost:5175, the simulator at http://localhost:8787. The first run
downloads the Aspire CLI bundle through `dnx`; nothing else to install, and Docker is not needed.

Real credentials instead of the simulator, stored once in the AppHost's user secrets (never committed):

```bash
cd backend/AffiliateOptimizer.AppHost
dotnet user-secrets set "AffiliateService:UseSimulator" "false"
dotnet user-secrets set "Parameters:affiliate-server-url" "<SERVER_URL>"
dotnet user-secrets set "Parameters:affiliate-app-key" "<APP_KEY>"
dotnet user-secrets set "Parameters:affiliate-app-secret" "<APP_SECRET>"
```

Run one host per credential pair: never scale `web` in the AppHost. If your shell still exports `SERVER_URL`,
`APP_KEY` or `APP_SECRET` from the old workflow, unset them: they would override the AppHost's wiring.

### Step by step (no Aspire)

1. Give the host the three credentials you were sent (never committed):

   ```bash
   cd backend/AffiliateOptimizer.Web
   dotnet user-secrets set "AffiliateService:ServerUrl" "<SERVER_URL>"
   dotnet user-secrets set "AffiliateService:AppKey" "<APP_KEY>"
   dotnet user-secrets set "AffiliateService:AppSecret" "<APP_SECRET>"
   ```

   The environment variables `SERVER_URL`, `APP_KEY` and `APP_SECRET` work as well.

2. Start the host (applies migrations, seeds the operator account, starts the loops):

   ```bash
   cd backend
   dotnet run --project AffiliateOptimizer.Web
   ```

3. Start the operator UI:

   ```bash
   cd frontend
   npm install
   npm run dev
   ```

   Open http://localhost:5174 and sign in as `operator@local` / `operator-dev-password` (the Development
   default; see the backend runbook to change it). Pick one or more models, tick "interleave" to compare them
   on the same traffic, and start a run.

Single-process alternative: `npm run build` in `frontend/` once, then the host serves the UI itself at
http://localhost:5175.

### No credentials? Use the simulator

`tools/affiliate-simulator` is a local stand-in for the Affiliate Service that implements the five endpoints
with the behaviours `API.md` describes and an invented, drifting world:

```bash
node tools/affiliate-simulator/server.mjs
```

then point the host at it: `SERVER_URL=http://localhost:8787 APP_KEY=ak_local APP_SECRET=as_local`.
`DISCLOSURE_DELAY_MS=8000` makes commissions arrive faster than the real one-minute delay;
`GET /_simulator/truth` reveals the hidden means so you can check what a model learned. Its numbers mean
nothing outside this repository.

## Tests

```bash
cd backend && dotnet test          # 220 tests: handlers, validators, entities, repositories, client, controllers, loops, composition
cd frontend && npm test            # unit tests of the UI helpers and the API client
cd frontend && npm run test:e2e    # drives the UI against a running host (needs credentials or the simulator)
```

`npm run test:e2e` starts the host and the UI itself, or reuses them when the AppHost is already running.

## Where things are

```
backend/            the .NET solution (see backend/README.md)
  AffiliateOptimizer.Domain/Features/   one folder per use case: Runs, Clicks, Commissions, Networks,
                                        Evaluation, Health, Models, LoopEvents
  AffiliateOptimizer.Domain/Common/Optimization/Models/   >>> the models: random, advertised-greedy,
                                                              epsilon-greedy, sliding-window-ucb <<<
frontend/           the React operator UI; src/api/operator.ts is its only I/O
tools/affiliate-simulator/   the local stand-in for the Affiliate Service
```

The host calls the Affiliate Service; the browser never sees the credentials. Every API response carries
`serverNow`, and the UI derives nothing from the browser clock.

## Leaving it running

Network behavior shifts over time, so a longer run gives a richer picture. The loop no longer lives in a tab:
close the browser, come back later, the run is still going, and a host restart continues it.

## Handing it back

The deliverables are this repository and [`WALKTHROUGH.md`](WALKTHROUGH.md). Questions are welcome at any point.
