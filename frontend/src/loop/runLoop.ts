/**
 * The click loop: pull a click, choose a network, route it. And, separately, poll for commissions.
 *
 * They are two independent loops on purpose. A click has two minutes to live and the feed discloses
 * a minute after routing, so binding them together would either delay routing behind a poll or stop
 * polling while a routing call was in flight. Keeping them separate is also what makes PAUSING
 * meaningful: pausing stops issuing new clicks but keeps polling, so clicks already in flight still
 * reach a terminal state instead of being abandoned mid-run.
 *
 * The loop owns POLICY — pacing, backoff, what to do about each error code. The client owns
 * transport. Nothing here retries blindly: a rejected routing already spent its click, and calling
 * again cannot undo that.
 */
import type { AffiliateServiceClient, Click, Merchant, Network } from '../api/client'
import { ApiError, TransportError, isRoutingRejectionCode } from '../api/errors'
import type { OptimizationModel } from '../models/random'
import { ClickLog, type ClickRecord } from './clickLog'
import { CLICK_INTERVAL_MS, POLL_INTERVAL_MS, backoffAfterFailure } from './backoff'

/** A failure worth showing above the feed, as distinct from a per-click errorCode. */
export interface FeedError {
  code: string
  message: string
  /** When the loop will try again. */
  until: number
  /** A persistent UNAUTHORIZED is not dismissible: nothing works until it is fixed. */
  dismissible: boolean
}

export interface LoopState {
  startedAt: number | null
  running: boolean
  merchants: Merchant[]
  networks: Network[]
  feedError: FeedError | null
  /**
   * Commissions the feed reported for clicks this page instance never issued -- almost always
   * because the page was reloaded and the server is replaying the credential pair's earlier
   * history. Counted rather than dropped silently; the UI says so.
   */
  skippedDisclosures: number
  /** Bumped whenever anything changed, so the UI can subscribe without diffing. */
  revision: number
}

export interface RunLoopOptions {
  client: AffiliateServiceClient
  optimizationModel: OptimizationModel
  log: ClickLog
  now?: () => number
  onChange: () => void
}

/** Matches the server's COMMISSION_FEED_PAGE_LIMIT. A full page means more is waiting. */
const FEED_PAGE_LIMIT = 100
const MAX_ROUTE_ATTEMPTS = 3

export class RunLoop {
  private readonly client: AffiliateServiceClient
  private readonly optimizationModel: OptimizationModel
  private readonly log: ClickLog
  private readonly now: () => number
  private readonly onChange: () => void

  private merchantNames = new Map<number, string>()
  private networkNames = new Map<string, string>()
  private cursor: string | null = null
  private clickTimer: ReturnType<typeof setTimeout> | null = null
  private pollTimer: ReturnType<typeof setTimeout> | null = null
  private clickFailures = 0
  private pollFailures = 0
  private stopped = false

  readonly state: LoopState = {
    startedAt: null,
    running: false,
    merchants: [],
    networks: [],
    feedError: null,
    skippedDisclosures: 0,
    revision: 0,
  }

  constructor(options: RunLoopOptions) {
    this.client = options.client
    this.optimizationModel = options.optimizationModel
    this.log = options.log
    this.now = options.now ?? (() => Date.now())
    this.onChange = options.onChange
  }

  private changed(): void {
    this.state.revision += 1
    this.onChange()
  }

  /**
   * Fetches the reference data once, then starts both loops.
   *
   * A failure here is fatal and propagates: without merchants and networks the client cannot pick an
   * eligible network, and routing blind would spend clicks on certain rejections.
   */
  async start(): Promise<void> {
    const [merchantList, networkList] = await Promise.all([
      this.client.merchants(),
      this.client.networks(),
    ])

    this.merchantNames = new Map(merchantList.map((entry) => [entry.merchantId, entry.name]))
    this.networkNames = new Map(networkList.map((entry) => [entry.id, entry.name]))
    this.state.merchants = merchantList
    this.state.networks = networkList
    // The first authenticated call just happened, which is what anchors the run counter.
    this.state.startedAt = this.now()
    this.state.running = true
    this.changed()

    this.scheduleClick(0)
    this.schedulePoll(POLL_INTERVAL_MS)
  }

  /** Stops issuing new clicks. Polling continues so in-flight clicks still resolve. */
  pause(): void {
    this.state.running = false
    if (this.clickTimer !== null) {
      clearTimeout(this.clickTimer)
      this.clickTimer = null
    }
    this.changed()
  }

  resume(): void {
    if (this.stopped || this.state.running) return
    this.state.running = true
    this.changed()
    this.scheduleClick(0)
  }

  /** Tears both loops down. Called on unmount. */
  stop(): void {
    this.stopped = true
    this.state.running = false
    if (this.clickTimer !== null) clearTimeout(this.clickTimer)
    if (this.pollTimer !== null) clearTimeout(this.pollTimer)
    this.clickTimer = null
    this.pollTimer = null
  }

  dismissFeedError(): void {
    if (this.state.feedError?.dismissible === false) return
    this.state.feedError = null
    this.changed()
  }

  private scheduleClick(delayMs: number): void {
    if (this.stopped || !this.state.running) return
    this.clickTimer = setTimeout(() => void this.issueAndRoute(), delayMs)
  }

  private schedulePoll(delayMs: number): void {
    if (this.stopped) return
    this.pollTimer = setTimeout(() => void this.poll(), delayMs)
  }

  private setFeedError(error: ApiError, waitMs: number): void {
    this.state.feedError = {
      code: error.code,
      message: error.message,
      until: this.now() + waitMs,
      // Nothing works until credentials are fixed, so this one stays on screen.
      dismissible: error.code !== 'UNAUTHORIZED',
    }
    this.changed()
  }

  private clearRecoverableFeedError(): void {
    if (this.state.feedError !== null && this.state.feedError.dismissible) {
      this.state.feedError = null
      this.changed()
    }
  }

  /**
   * A route response can be lost after the server commits the click. Replaying the identical
   * click/network pair is safe because the server faithfully returns its original outcome.
   */
  private async routeWithRecovery(clickId: string, networkId: string) {
    for (let attempt = 1; attempt <= MAX_ROUTE_ATTEMPTS; attempt += 1) {
      try {
        return await this.client.route(clickId, networkId)
      } catch (cause) {
        if (!(cause instanceof TransportError) || attempt === MAX_ROUTE_ATTEMPTS || this.stopped) {
          throw cause
        }
        await new Promise<void>((resolve) => {
          setTimeout(resolve, backoffAfterFailure(attempt, undefined))
        })
        // stop() may have happened while the retry backoff timer was pending.
        if (this.stopped) throw cause
      }
    }
    throw new Error('routeWithRecovery exhausted without returning or throwing')
  }

  /** One turn of the click loop. */
  private async issueAndRoute(): Promise<void> {
    if (this.stopped || !this.state.running) return

    let click: Click
    try {
      click = await this.client.nextClick()
      this.clickFailures = 0
      this.clearRecoverableFeedError()
    } catch (cause) {
      this.clickFailures += 1
      const retryAfter = cause instanceof ApiError ? cause.retryAfterSeconds : undefined
      const wait = backoffAfterFailure(this.clickFailures, retryAfter)
      if (cause instanceof ApiError) this.setFeedError(cause, wait)
      this.scheduleClick(wait)
      return
    }

    const record: ClickRecord = {
      clickId: click.clickId,
      merchantId: click.merchantId,
      merchantName: this.merchantNames.get(click.merchantId) ?? `Merchant ${click.merchantId}`,
      issuedAt: Date.parse(click.issuedAt),
      expiresAt: Date.parse(click.expiresAt),
      routedAt: null,
      routedNetworkId: null,
      routedNetworkName: null,
      commission: null,
      disclosedAt: null,
      errorCode: null,
    }
    this.log.append(record)
    this.changed()

    const decision = this.optimizationModel.choose(click.merchantId, this.state.networks)
    if (decision === null) {
      // Nothing covers this merchant. Let it lapse rather than spend it on a certain rejection.
      this.scheduleClick(CLICK_INTERVAL_MS)
      return
    }

    try {
      const acknowledgment = await this.routeWithRecovery(click.clickId, decision.networkId)
      this.log.update(click.clickId, {
        routedAt: Date.parse(acknowledgment.routedAt),
        routedNetworkId: acknowledgment.networkId,
        routedNetworkName: this.networkNames.get(acknowledgment.networkId) ?? acknowledgment.networkId,
      })
    } catch (cause) {
      if (this.stopped) return
      if (cause instanceof ApiError && isRoutingRejectionCode(cause.code)) {
        /*
         * The click is spent. Record the rejection against the row rather than retrying: routing is
         * final, and the same code will arrive in the feed shortly.
         */
        this.log.update(click.clickId, {
          routedAt: this.now(),
          routedNetworkId: decision.networkId,
          routedNetworkName: this.networkNames.get(decision.networkId) ?? decision.networkId,
          errorCode: cause.code,
        })
      } else if (cause instanceof ApiError) {
        // Request/auth/lookup failures do not commit a route. Surface them globally, but leave the
        // click un-routed so the local ledger does not invent a rejection the server never recorded.
        this.setFeedError(cause, backoffAfterFailure(1, cause.retryAfterSeconds))
      } else {
        const wait = backoffAfterFailure(MAX_ROUTE_ATTEMPTS, undefined)
        console.error('Route outcome remains unconfirmed after safe retries', cause)
        this.state.feedError = {
          code: 'ROUTE_UNCONFIRMED',
          message:
            'The route response was lost after three attempts. Commission polling will reconcile ' +
            'the click if the server accepted it.',
          until: this.now() + wait,
          dismissible: true,
        }
      }
      // The click loop moves on. The feed can still reconcile a route whose response was lost.
    }

    this.changed()
    this.scheduleClick(CLICK_INTERVAL_MS)
  }

  /** One turn of the commission poll. */
  private async poll(): Promise<void> {
    if (this.stopped) return

    try {
      const feed = await this.client.commissions(this.cursor)
      this.pollFailures = 0

      for (const row of feed.commissions) {
        // Routing fields make the feed a recovery path. If POST /route completed but every
        // response was lost, this event still repairs the local row before applying its outcome.
        const folded = this.log.update(row.clickId, {
          routedAt: Date.parse(row.routedAt),
          routedNetworkId: row.routedNetworkId,
          routedNetworkName: this.networkNames.get(row.routedNetworkId) ?? row.routedNetworkId,
          commission: row.commission,
          errorCode: row.errorCode,
          disclosedAt: Date.parse(row.disclosedAt),
        })
        // Not an error: the feed is scoped to the credential pair, not to this page load, so a
        // reload replays commissions for clicks a previous instance issued. See ClickLog.update.
        if (folded === null) this.state.skippedDisclosures += 1
      }

      // Stored after EVERY poll, including empty ones. It comes back unchanged when nothing is new,
      // and discarding it would re-read the feed from the beginning and double-count.
      this.cursor = feed.nextCursor

      if (feed.commissions.length > 0) this.changed()
      this.clearRecoverableFeedError()

      // A full page means more is waiting; drain it before returning to the normal cadence.
      this.schedulePoll(feed.commissions.length >= FEED_PAGE_LIMIT ? 0 : POLL_INTERVAL_MS)
    } catch (cause) {
      this.pollFailures += 1
      const retryAfter = cause instanceof ApiError ? cause.retryAfterSeconds : undefined
      const wait = backoffAfterFailure(this.pollFailures, retryAfter)

      if (cause instanceof ApiError) {
        this.setFeedError(cause, wait)
        // A tampered or stale cursor cannot be recovered by retrying the same value. Restart the
        // feed: rows are keyed by click id, so re-reading them is harmless.
        if (cause.code === 'INVALID_CURSOR') this.cursor = null
      } else {
        /*
         * A failure that is not the server's: a bug in this client, or a browser-level fault. It
         * MUST be visible. An earlier version silently retried these, and the result was a client
         * that displayed a healthy-looking feed while quietly failing every poll -- which is much
         * worse than an ugly error strip.
         */
        console.error('Commission poll failed for a non-API reason', cause)
        this.state.feedError = {
          code: 'CLIENT_ERROR',
          message: `The commission poll failed inside the client: ${
            cause instanceof Error ? cause.message : String(cause)
          }`,
          until: this.now() + wait,
          dismissible: true,
        }
        this.changed()
      }
      this.schedulePoll(wait)
    }
  }
}
