# The Affiliate Service API

This is the human-readable guide to the API. The exact contract lives in `openapi.json`.

It assumes you know what a click, a network and the disclosure delay *mean*. That is
[`CONCEPTS`](CONCEPTS.md). What we are asking you to build with them is
[`BRIEF`](BRIEF.md).

## Where the contract lives

The API is described by an **[OpenAPI 3.1](https://spec.openapis.org/oas/latest.html)** document at
[`openapi.json`](openapi.json). OpenAPI is a machine-readable format for HTTP endpoints, request and
response shapes, status codes, and errors. The specification is authoritative if prose disagrees
with it.

Use this guide for behavior and relationships. Use `openapi.json` when you need an exact field,
status, or schema. The TypeScript declarations in `frontend/src/api/schema.d.ts` are generated from
that specification and are the quickest code-level view of the same contract.

## Base URL and authentication

The base URL comes from `SERVER_URL` in your `.env`.

Every endpoint requires authentication as a pair of headers, sent on every call:

```
X-App-Key: <appKey>
X-App-Secret: <appSecret>
```

Both values are issued to you out of band. There is no login endpoint, no token exchange,
and no refresh. Missing or invalid credentials return `UNAUTHORIZED` (401).

## The endpoints

Five endpoints, which together are the whole exercise.

### `GET /merchants`

The reference table of merchants: `merchantId` and `name`. Fixed for the life of your run,
so fetch it once at startup.

The response is `{ merchants: Merchant[] }`, where each merchant contains:

| Field | Type | Notes |
| --- | --- | --- |
| `merchantId` | integer | Stable merchant identifier. |
| `name` | string | Display name. |

### `GET /networks`

Every network, public fields only: `id`, `name`, `advertisedRate`, and
`supportedMerchantIds`.

The response is `{ networks: Network[] }`, where each network contains:

| Field | Type | Notes |
| --- | --- | --- |
| `id` | string | Stable network identifier; send this as `networkId` when routing. |
| `name` | string | Display name. |
| `advertisedRate` | number | Public, non-binding headline CPC. |
| `supportedMerchantIds` | integer[] | Merchants this network accepts. |

`supportedMerchantIds` is **coverage**, and it is the one thing here you can treat as
fact. Routing a click to a network that does not cover its merchant is an avoidable error
that still consumes the click.

### `POST /clicks/next`

Issues one click. The response contains:

| Field | Type | Notes |
| --- | --- | --- |
| `clickId` | string | Stable identifier used in the route path and later commission events. |
| `merchantId` | integer | Determines which networks are eligible. |
| `issuedAt` | ISO 8601 string | When the click was issued. |
| `expiresAt` | ISO 8601 string | Route before this time or lose the click. |

Clicks expire two minutes after issue, and this endpoint is rationed at 20 per minute
bursting to 40. Those two limits together are what make the click stream a scarce
resource rather than an infinite one.

### `POST /clicks/{clickId}/route`

Body `{ networkId }`. Commits the click to one network.

This endpoint **never returns the commission.** Routing is final: a click routes exactly once.
Calling it again faithfully replays the original accepted or rejected outcome with
`alreadyRouted: true`. Repeating the identical click/network request after an ambiguous transport
failure is safe.

An accepted response contains:

| Field | Type | Notes |
| --- | --- | --- |
| `outcome` | `accepted` | Discriminator for a successful routing. |
| `clickId` | string | The routed click. |
| `networkId` | string | The network the click was committed to. |
| `routedAt` | ISO 8601 string | When routing was accepted. |
| `alreadyRouted` | boolean | `true` when this call found an existing routing and changed nothing. |
| `commissionAvailableAt` | ISO 8601 string | When its result becomes eligible to appear in the commission feed. |

An expired or unsupported route returns HTTP 410 or 422 with `outcome: rejected`, the committed
`clickId`, `networkId`, and `routedAt`, an `errorCode`, a human-readable `message`, and
`alreadyRouted`. Retrying that route returns the same status and error code with
`alreadyRouted: true`; it never disguises a rejection as an accepted route.

### `GET /commissions?cursor={token}`

The only way you learn what a click earned. Returns everything that has happened since your last
read, plus a `nextCursor` to pass on the following call. Omit the cursor to start from the beginning.
The response shape is `{ commissions: CommissionRow[], nextCursor: string | null }`.

Each routed click produces exactly one row here, once its outcome is disclosed, and that row never
changes afterwards. A click you have already seen will not come back with a different number.

Each row carries:

| Field | Notes |
| --- | --- |
| `clickId` | Which click. Unique in this feed. |
| `kind` | `settlement` or `rejection`. The discriminator for the two exact row shapes. |
| `state` | `settled` if it earned a commission, `rejected` if the routing was ineligible or late. |
| `commission` | Positive on a settlement; exactly null on a rejection. |
| `errorCode` | Exactly null on a settlement; `MERCHANT_NOT_SUPPORTED` or `CLICK_EXPIRED` on a rejection. |
| `merchantId`, `routedNetworkId` | As routed. |
| `routedAt` | When the click was committed. Lets the feed repair a locally uncertain route. |
| `disclosedAt` | When this row became visible: one minute after an accepted route, immediately for a rejection. |

Three behaviors to design around:

- **Commissions are disclosed one minute after routing.** Every decision you make rests on
  information that is at least that stale. This is the central constraint of the exercise: you
  choose a network now and find out what it paid a minute from now.
- **An empty result is normal**, not an error and not a signal that anything is wrong. Poll again.
- **A rejection appears here too**, carrying no commission. It is the record of a click you spent
  without earning anything, and it is worth counting separately from a click that simply paid little.

Treat `nextCursor` as an **opaque blob**. It is a server-signed token; it is deliberately
not a timestamp, an offset, or an ID, and anything you infer from its contents will be
wrong. Store it and send it back. This endpoint is rationed at 60 per minute.

**Store it after every call, including the empty ones.** The cursor comes back unchanged when
there was nothing new, so a client that discards it on an empty page starts the feed over on
its next poll and double-counts every commission it had already banked.

**The feed is scoped to your credentials, not to your session.** If your client keeps its
record of the run in memory, reloading it starts that record empty while the server quite
correctly replays every commission your credential pair has ever earned. The shipped client
counts rows it has no record of as skipped rather than choking on them. Worth knowing if you
rebuild the log, and worth fixing properly if you would rather the record survived a reload.

## Errors

Authentication, validation, lookup, cursor, and throttle errors carry a machine-readable `code` and
a human-readable `message`. A committed route rejection carries the same stable value in
`errorCode` alongside its routing metadata. The supplied client normalizes either form into an
`ApiError`.

| Code | HTTP | Where | Meaning |
| --- | --- | --- | --- |
| `MERCHANT_NOT_SUPPORTED` | 422 | route | This network does not cover the click's merchant. Avoidable: coverage is public. |
| `CLICK_EXPIRED` | 410 | route | A route was attempted after the two-minute deadline. |
| `CLICK_NOT_FOUND` | 404 | route | Unknown click, or one issued to another candidate. |
| `NETWORK_NOT_FOUND` | 404 | route | Unknown network. |
| `INVALID_REQUEST` | 400 | route | The request body does not match the contract. |
| `INVALID_CURSOR` | 400 | commissions | Malformed or tampered cursor token. |
| `RATE_LIMITED` | 429 | next, commissions | Throttle exceeded. Carries `retryAfterSeconds`. |
| `UNAUTHORIZED` | 401 | all | Missing, malformed, or revoked credentials. |

Rejections are visible twice on purpose. A committed route rejection comes back synchronously from
the route call *and* appears in the commissions feed, so every click you spent resolves somewhere
you can observe in one place. If you are keeping your own ledger of clicks, make sure it tolerates
seeing the same outcome by both paths.

**You will not see a single rejection to begin with, and that is correct.** The shipped random
optimization model only ever picks networks that cover the click's merchant, so it never earns a
`MERCHANT_NOT_SUPPORTED`. Rejected outcomes start appearing the moment an optimization model routes
outside published coverage, which is precisely the mistake the rule exists to punish. If you want
to watch the path work, route a click deliberately to a network whose `supportedMerchantIds`
excludes its merchant: you get a 422 with the error code, the click is spent, and the row turns up in
the feed with a null commission.

## Rate limits

| Endpoint | Sustained | Burst |
| --- | --- | --- |
| `POST /clicks/next` | 20 / min | 40 |
| `GET /commissions` | 60 / min | 60 |

Both are token buckets, per candidate. Routing is not separately throttled, since it is
already bounded by how many clicks exist. Exceeding a limit returns `RATE_LIMITED` with
`retryAfterSeconds`.
