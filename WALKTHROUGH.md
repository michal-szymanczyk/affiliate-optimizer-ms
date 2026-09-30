# Walkthrough

What was built, why it has this shape, what the evidence can and cannot support, and what was left out.
Read [`README.md`](README.md) first for how to run it.

## The direction in one paragraph

The shipped client did the whole loop in a browser tab and was naive in exactly one place: the routing choice.
I moved the loop, the credentials and the observation store into a .NET 10 host that follows
[`ArchitectureSimple.md`](ArchitectureSimple.md), kept the React app as a pure operator UI over a small API,
and treated "which model decided, on what evidence, and can we trust the comparison" as the product. The
result is three things the brief asks for that a browser tab cannot give: runs that survive a reload and a
restart, an evidence store every model reads from and every screen explains from, and a comparison design
(interleaving) with a verdict that refuses to claim more than the data supports.

## Architecture (how it maps to the template)

```
backend/
  Common/Common.Domain        request pipeline (Appendix A verbatim), validation, exceptions + middleware,
                              ISystemTime, list/paging support
  Common/Common.Repository    UnitOfWork<TDbContext>, EntityRepository, feature-repository registration
  AffiliateOptimizer.Domain   Features/<Area>/<UseCase>/ slices; Common/ holds the Affiliate Service contract,
                              the optimization abstractions and the four models, pacing, click-state derivation
  AffiliateOptimizer.Repository  AppDbContext (+ Identity), entities with behaviour, one repository class per area
  AffiliateOptimizer.Web      Startup (§8.1 order), thin controllers, Identity login, typed HttpClient,
                              two hosted loops, health checks, SPA hosting
  Tests/                      handler / validator / entity / repository (EF in-memory) / client / controller /
                              loop / composition tests, 220 of them
frontend/                     the React operator UI (api/operator.ts is its only I/O)
tools/affiliate-simulator/    a local stand-in for the Affiliate Service, for demos and end-to-end runs
```

The two loops are `BackgroundService`s that own scheduling only. Each turn is one Domain command sent through
the dispatcher in its own DI scope (`ClicksIssueAndRoute`, `CommissionsPoll`); the command's result says how
long to wait. All policy (what to do on each failure, how long to back off, when to give up on a route) lives in
the handlers, where it is unit-tested with mocks and a fake clock. The original client's loop tests were ported
one for one and extended.

Run state lives in the database. The click loop reads the run's status every turn, so pause/resume cannot
start a second click chain (a defect of the original client), and a restart continues the run.

## The models

All models implement one interface and are **pure**: `Decision? Choose(DecisionContext)`. The context carries
the eligible networks (coverage already applied, once, in `Eligibility`), the arm's own evidence per network
rebuilt from the store, the clock and a random source. No model keeps state between clicks. That is what makes
them survive a restart, replayable over recorded evidence, and testable with pinned draws.

| Model | Rule | Why it is here |
| --- | --- | --- |
| `random` | uniform among covering networks | the control; a port of the shipped model |
| `advertised-greedy` | highest advertised CPC | the "trust the claim" baseline; its claim gap measures honesty |
| `epsilon-greedy` | explore with ε, else best recent mean; advertised rate as optimistic prior for unseen networks | a simple learner that follows drift through the recent window |
| `sliding-window-ucb` | recent mean + C·√(ln N / n); unobserved networks first | principled exploration that keeps re-checking networks that left the window |

Every decision is recorded with the model's name and version, its rationale and the score it gave every
candidate (`scoresJson`), so the operator can answer "why this network" for any row.

Evidence is **per arm**: in an interleaved run, `epsilon-greedy` learns only from the clicks it decided. That
halves its learning rate, and it is deliberate: it is what makes the arms comparable.

## Evaluation: what is honest to claim

The unit of comparison is *mean realized CPC per settled click of an arm's own decisions inside one run*.

- **Interleaving** (round-robin by click ordinal) gives every arm the same clock window and, statistically, the
  same merchant mix. Back-to-back runs cannot promise that: the hidden behaviour changes within half an hour.
- **Provenance** on every run: models and versions, parameters JSON, the network catalogue as seen at start
  (advertised rates and coverage), credential fingerprint, app version, start and stop time.
- **Uncertainty**: mean, standard error and a 95% interval per arm; `Inconclusive` until every arm has 30
  settled clicks; `Leading` only when the best arm's interval lies entirely above every other arm's; otherwise
  `NoSeparation`. An arm whose first and second halves disagree gets a drift caveat and can never be
  `Leading`.
- **What it cannot say**: there is no counterfactual. The UI prints that caveat on every comparison, and the
  verdict is always shown with its caveats, never as a bare winner.
- **A known blind spot of the drift flag**: it compares an arm's first and second halves, so for the `random`
  arm it fires as readily on a different mix of networks and merchants as on a real change in the world. It is
  a prompt to look at the Networks panel, where drift is measured per network, not a finding on its own.

Per network, the Networks panel shows advertised vs realized, the claim gap, the recent mean and trend, and
three flags: **thin** (fewer than 10 settled), **stale** (no disclosure in 5 minutes), **changing** (recent
mean differs from all-time mean by more than 1.96 SE). They exist so a lucky observation is not read as a trend.

## The operator experience

One screen, top to bottom: run controls (which models, interleaved or not, pause / resume / stop; the status
line says `paused · polling continues` because that is the truth), a health strip with a **diagnosis** that
localises a fault the way the brief asks (credentials → application → traffic → feed → model), the counters,
the open loop events (the persisted successor of the single "feed error" slot; UNAUTHORIZED cannot be
dismissed), and tabs for the click feed (server-paged, sortable, filterable, with the decision's rationale and
scores), the network insight, the comparison, and the run history to scope everything to an earlier run.

Every response carries `serverNow`. The browser clock decides nothing.

## Observability

- Operational: last click issued / routed / disclosed, last poll and last successful poll, cursor present,
  consecutive failures per loop, loop heartbeats (the only in-process state), awaiting and overdue disclosures,
  counts of rate limits / transport failures / unconfirmed routes / model errors / rejections in the last five
  minutes, plus `/status` health checks for SQL Server and the Affiliate Service.
- Decision: per-click rationale and scores, per-network claim gap and flags, per-arm intervals and drift.
- Orphan disclosures (feed rows for clicks this store never issued, i.e. history replayed after a database
  reset) are stored and counted on the Counters and Health screens instead of being skipped silently.
- Under the AppHost (`backend/AffiliateOptimizer.AppHost`): structured logs, a trace per Affiliate Service call
  and per API request, HTTP and runtime metrics in the Aspire dashboard, and the health of the simulator, the
  host and the UI side by side.

## Evolution

- **Add a model**: one class implementing `IOptimizationModel`; the assembly scan registers it, the catalog
  validates its name, the run stores its parameters. Nothing else changes.
- **Add a use case**: one folder under `Features/<Area>/` with request, handler, validator and `IRepository`;
  the repository class of the area implements the interface; registration is by scan; the composition test
  proves the handler resolves.
- **Run elsewhere**: the loops are hosted services; the host runs unattended with `Loop:AutoStartModel`.
- **Left open on purpose**: per-run model parameter overrides (the provenance column already supports it),
  shared-evidence runs, a run lease so two hosts cannot share one credential pair, calibrating host time
  against the server's `Date` header, a scheduler that pauses arms whose evidence is stale.

## Deviations from the template, with reasons

1. No `Common.Communication` / Contracts: the Affiliate Service is a third-party REST API, so it is a Domain
   interface implemented by a typed `HttpClient` in `Web/Infrastructure`, with exactly one attempt per call.
2. The loops are `BackgroundService`s that depend on `IDispatcher` only (§4.7 applied to a concept the
   template does not have).
3. `ExceptionHandlerMiddleware` takes pluggable mappers so upstream failures become 502/503 without
   `Common.Domain` knowing the application's exception types.
4. Health checks use ASP.NET Core's `AddHealthChecks` at `/status`; the audit log, file manager, cryptography,
   cache, exports and legacy database are dropped (all optional in the template).
5. `InMemoryDataHelper` and `ControllerTestsBase` live in the test project: there is one consumer.
6. `ModelsGetAll` is a list query backed by the catalog, not by EF: the list of models is code.
7. `Click` is keyed by the server's string id; the feed and the route endpoint key by it.
8. Development orchestration and telemetry come from Aspire rather than the template's Dockerfile / `deploy.yaml`
   (§11) and Application Insights (§8.1 step 1). `AffiliateOptimizer.AppHost` starts the simulator, the host and
   the Vite server; publishing is unchanged. `AffiliateOptimizer.ServiceDefaults` registers OpenTelemetry from
   `Program.cs` before `Startup`, so step 1 still comes first without `Startup` knowing about it (the composition
   test builds `Startup` on a bare `ServiceCollection`). The defaults are trimmed: no service discovery (the host
   calls one configured URL) and no standard resilience handler (deviation 1). `/health` and `/alive` exist in
   Development next to `/status`.

## What I did not build

A route-outside-coverage "chaos" button, shared-evidence runs, a scheduler, alerts, and a proper design pass on
the UI. The brief's four-hour guideline was exceeded, mostly by the template's mandatory scaffolding (Common
libraries, Identity, migrations); the work was phased so that a working system existed after the loop and the
operator read side, with evaluation, the fourth model and the docs as the last phases.

## How AI was used

Claude Code did most of the typing: exploration of the original client (it produced a line-referenced catalogue
of every loop branch, which became the port's test list), the .NET implementation phase by phase, the tests,
the simulator and this document. I checked: every ported test against the original TypeScript expectations,
the loop policies against `API.md` (cursor after empty polls, replayed routes, rejections visible twice), the
statistics by hand for the fixed cases in the tests, the smoke run (login, 401/400/404/412/503 paths) and an
interleaved run end to end against the simulator. Where it got in the way: it happily generated code that
compiled against my Polish machine's culture (comma decimals in rationales) and it needed several rounds to
respect the template's naming of nested slice types. What I would not trust it with here: the verdict rule and
the evidence semantics; those I decided and wrote the tests for first.

## Verification performed

- `dotnet test`: 220 tests green (handlers, validators, entities, repositories over EF in-memory, HTTP client
  normalisation, controllers, hosted loops, composition).
- Smoke run of the real host: `/status`, login with antiforgery, 401 for anonymous API calls, validation 400
  with field details, 412 on a second active run, 503 when the service is unreachable, 404 for unknown routes.
- End-to-end run against `tools/affiliate-simulator` with an 8 s disclosure delay: interleaved
  `random + epsilon-greedy`, clicks routed and settled, evidence and rationales recorded, health `healthy`,
  comparison `inconclusive` with the sample-floor caveat, restart continuing the run with zero orphans.
- Frontend: `npm run typecheck`, `npm test` (35 tests: API client, polling hook, formatting) and
  `npm run build` green.
- Operator UI driven by hand in a browser against the simulator: sign-in, starting an interleaved run from the
  form, rows alternating between the two models, settlements and counters following, the "why" expander with
  the score table, the Networks and Compare tabs, pause (`paused · polling continues`), stop with confirmation,
  and the run history scoping the panels. One defect found and fixed on the way: the run-history query sorted
  on a member of a constructor projection, which EF Core cannot translate; the projection now uses an object
  initializer like the click list.
- Aspire: `dotnet build` of the solution with the AppHost and ServiceDefaults at zero warnings (warnings are
  errors), the 220 backend and 35 frontend tests unchanged, `dotnet run --project AffiliateOptimizer.AppHost`
  acquiring the CLI bundle through `dnx` and starting the simulator (8787), the host (5175) and Vite (5174) with
  all health probes at 200 (`/status` reporting self, sql and affiliate-service healthy), sign-in and a resumed
  run through the proxied UI, Vite's HMR websocket connected through the DCP proxy, and the dashboard showing
  the four resources, the injected environment, traces per Affiliate Service call, structured logs and metrics.
