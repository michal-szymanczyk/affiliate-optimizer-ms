/**
 * The typed Affiliate Service client.
 *
 * Request and response shapes come from `schema.d.ts`, which is generated from the committed
 * `openapi.json` (`npm run api:generate`), so the types here cannot drift from the contract the
 * server enforces.
 *
 * What this layer does NOT do is retry, back off, or make policy decisions. It performs one call
 * and either returns the response or throws an ApiError carrying the server's code. Deciding what
 * to do about a `RATE_LIMITED` belongs to the loop, which is the only thing that knows whether it
 * is issuing a click or polling a feed.
 */
import type { paths } from './schema'
import { ApiError, TransportError } from './errors'
import type { Config } from './config'

type Json<T> = T extends { content: { 'application/json': infer B } } ? B : never

export type Merchant = Json<paths['/merchants']['get']['responses'][200]>['merchants'][number]
export type Network = Json<paths['/networks']['get']['responses'][200]>['networks'][number]
export type Click = Json<paths['/clicks/next']['post']['responses'][200]>
export type RouteAcknowledgment = Json<
  paths['/clicks/{clickId}/route']['post']['responses'][200]
>
export type CommissionFeed = Json<paths['/commissions']['get']['responses'][200]>
export type CommissionRow = CommissionFeed['commissions'][number]

interface ErrorBody {
  code?: string
  errorCode?: string
  message?: string
  retryAfterSeconds?: number
}

export class AffiliateServiceClient {
  private readonly config: Config
  private readonly fetchImpl: typeof fetch

  /**
   * `fetchImpl` is injected so the unit tests can drive this without a server.
   *
   * The default is WRAPPED rather than passed by reference: `fetch` is a method on `window`, and
   * storing it as a property then calling `this.fetchImpl(...)` invokes it with the wrong receiver,
   * which the browser rejects with "Illegal invocation".
   */
  constructor(config: Config, fetchImpl?: typeof fetch) {
    this.config = config
    this.fetchImpl = fetchImpl ?? ((input, init) => globalThis.fetch(input, init))
  }

  private async request<T>(path: string, init: RequestInit = {}): Promise<T> {
    const url = `${this.config.serverUrl}${path}`

    let response: Response
    try {
      response = await this.fetchImpl(url, {
        ...init,
        headers: {
          'X-App-Key': this.config.appKey,
          'X-App-Secret': this.config.appSecret,
          ...(init.body === undefined ? {} : { 'Content-Type': 'application/json' }),
          ...init.headers,
        },
      })
    } catch (cause) {
      throw new TransportError(url, cause)
    }

    const text = await response.text()
    let parsed: unknown = null
    if (text.length > 0) {
      try {
        parsed = JSON.parse(text)
      } catch {
        throw new ApiError(
          response.status,
          'UNREADABLE_RESPONSE',
          `${path} returned a ${response.status} that is not JSON: ${text.slice(0, 200)}`,
        )
      }
    }

    if (!response.ok) {
      const body = (parsed ?? {}) as ErrorBody
      throw new ApiError(
        response.status,
        body.code ?? body.errorCode ?? 'UNKNOWN',
        body.message ?? `${path} returned ${response.status}.`,
        body.retryAfterSeconds,
      )
    }

    return parsed as T
  }

  /** Fixed for the life of a run. Fetch once at startup. */
  async merchants(): Promise<Merchant[]> {
    return (await this.request<{ merchants: Merchant[] }>('/merchants')).merchants
  }

  /**
   * Every network, public fields only.
   *
   * `supportedMerchantIds` is the one thing here that can be treated as fact. `advertisedRate` is
   * a claim.
   */
  async networks(): Promise<Network[]> {
    return (await this.request<{ networks: Network[] }>('/networks')).networks
  }

  /** Issues one click. Throttled: 20/min, bursting to 40. */
  async nextClick(): Promise<Click> {
    return this.request<Click>('/clicks/next', { method: 'POST' })
  }

  /**
   * Routes a click. Final and irreversible.
   *
   * Never returns a commission -- that arrives in the feed a minute later. A repeat call for an
   * already-routed click returns the existing routing with `alreadyRouted: true` rather than an
   * error, so a retry through a timeout is safe.
   */
  async route(clickId: string, networkId: string): Promise<RouteAcknowledgment> {
    return this.request<RouteAcknowledgment>(`/clicks/${encodeURIComponent(clickId)}/route`, {
      method: 'POST',
      body: JSON.stringify({ networkId }),
    })
  }

  /**
   * Commissions disclosed since the given cursor.
   *
   * Store `nextCursor` after every call. It comes back unchanged when there was nothing new, so a
   * caught-up client keeps its place; discarding it would re-read the whole feed and double-count.
   */
  async commissions(cursor: string | null): Promise<CommissionFeed> {
    const query = cursor === null ? '' : `?cursor=${encodeURIComponent(cursor)}`
    return this.request<CommissionFeed>(`/commissions${query}`)
  }
}
