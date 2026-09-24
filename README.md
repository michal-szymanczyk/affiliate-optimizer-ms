# Affiliate Optimizer

Every three seconds, a click arrives for a merchant. Several affiliate networks may accept it.
Each advertises a cost per click (CPC), but the amount it actually pays is delayed, noisy, and
changes over time. For each click, you choose one network and later see only what that network paid.
Clicks are rationed, so learning from one choice means giving up the others.

The supplied client already closes that loop by choosing randomly. Your challenge is to make its
routing decisions better informed while turning it into a system a colleague could extend, operate,
and trust.

## Start here

| Document | What it gives you |
| --- | --- |
| [`CONCEPTS`](CONCEPTS.md) | **The problem space.** What affiliate routing is, what each concept means, and the three properties that make the decision hard. Start here. |
| [`BRIEF`](BRIEF.md) | **The ask.** What we are looking for, the shape of the challenge, and what to hand back. |
| [`API`](API.md) | **The reference.** Five endpoints, the error codes, the rate limits, and a link to the complete machine-readable contract. |

Read `CONCEPTS` and `BRIEF` before deciding what to build. Keep `API` nearby as a reference when you
need an exact behavior or response shape.

## Getting it running

Prerequisite: Node.js 22.13 or newer and npm 10 or newer.

```bash
cd frontend
cp .env.example .env    # paste in the credentials we sent you
npm install
npm run dev             # http://localhost:5174
```

Three values in `.env`, all issued to you out of band:

| | |
| --- | --- |
| `APP_KEY` | Public half of your credential pair. |
| `APP_SECRET` | Private half. Both go on every API call as headers. |
| `SERVER_URL` | The Affiliate Service you are routing against. |

If any are missing the client says so on screen rather than failing quietly. Vite only reads
`.env` at startup, so restart the dev server after editing it.

```bash
npm test               # unit tests
npm run typecheck
npm run test:e2e       # drives the client against the real API
npm run api:generate   # regenerate the typed client from openapi.json
```

`api:generate` writes `src/api/schema.d.ts`. You only need it if you change the specification,
which you should not need to do. [`API`](API.md) is the human-readable guide and
[`openapi.json`](openapi.json) is the complete machine-readable contract.

Port 5174 is pinned so it does not collide with anything else we run locally, and the server
permits that origin.

## Where things are

The client does the whole loop end to end: authenticates, fetches the merchant and network
lists, pulls a click, picks a network, routes it, polls for commissions as they are disclosed,
and shows them with a running total. Rate limits, paging, and safe recovery after an ambiguous
route response are handled, so you should not need to revisit them unless you want to.

It is naive at exactly one point: the **optimization model** choosing where a click goes. The
shipped optimization model filters the networks down to those covering the click's merchant, then
picks one uniformly at random.

```
frontend/src/
  api/
    schema.d.ts    generated from openapi.json, do not hand-edit
    client.ts      typed HTTP client. One call in, one response or one thrown error out.
    config.ts      reads .env, fails loudly when something is missing
    errors.ts      the server's error codes, as types
  loop/
    runLoop.ts     two loops: issue-and-route, and poll-for-commissions
    clickLog.ts    the client's own record of the run. State is derived, never stored.
    backoff.ts     pacing and retry arithmetic, pure
  models/
    random.ts      >>> the random optimization model. The naive bit, and the interesting file. <<<
  views/           the example UI
  lib/             formatting, filtering, sorting
```

One thing to know before you touch the loop: a route request may reach the server even when its
response does not reach the browser. Repeating the identical click/network request is safe because
the server replays the original accepted or rejected outcome. The commission feed also carries the
routing fields needed to reconcile an uncertain local row. [`API`](API.md) has the exact shapes.

## Leaving it running

Network behavior shifts over time, so a longer run gives a richer picture, though a short run is
enough to see the shape of the problem. Browsers throttle timers in background tabs, so a hidden tab
may collect fewer clicks than you expect.

## Handing it back

The deliverables are your committed repo and a walkthrough of your thinking. The format of that
walkthrough is yours; [`BRIEF`](BRIEF.md#what-to-hand-back) has the details.

Questions are welcome at any point, so please reach out.
