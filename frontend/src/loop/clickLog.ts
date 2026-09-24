/**
 * The client's own record of every click it has issued.
 *
 * This is the client's memory of the run, and it is the only place the UI reads from. It exists
 * because the server will not tell you what you did: `GET /commissions` reports only what has been
 * disclosed, so "clicks I issued but have not heard about" is a fact only the client holds.
 *
 * STATE IS DERIVED, NEVER STORED -- the same rule the server follows. A row holds timestamps and
 * whatever the feed reported; `clickState` turns those plus the current time into a state. Storing
 * a state would mean writing `expired` on a timer for every click the loop did not get to.
 */

export type ClickState = 'issued' | 'routed' | 'settled' | 'rejected' | 'expired'

export interface ClickRecord {
  clickId: string
  merchantId: number
  merchantName: string
  issuedAt: number
  expiresAt: number
  /** Set when the routing call succeeded, or when it was rejected -- both spend the click. */
  routedAt: number | null
  routedNetworkId: string | null
  routedNetworkName: string | null
  /** From the commission feed, once disclosed. */
  commission: number | null
  disclosedAt: number | null
  /** A §5 code: from a synchronous rejection, or from the feed. */
  errorCode: string | null
}

/**
 * A click's state, derived.
 *
 * Order matters, and mirrors the server's own derivation:
 *   1. `rejected` outranks everything -- a click spent on a failed routing is a different fact
 *      from one left to lapse.
 *   2. `settled` once the commission has been disclosed.
 *   3. `routed` while it is still withheld.
 *   4. `expired` for a click never routed and now past its expiry.
 *   5. `issued` otherwise: live, awaiting a decision.
 */
export function clickState(click: ClickRecord, now: number): ClickState {
  if (click.errorCode !== null) return 'rejected'
  if (click.routedAt !== null) {
    if (click.disclosedAt !== null && click.disclosedAt <= now) return 'settled'
    return 'routed'
  }
  if (click.expiresAt <= now) return 'expired'
  return 'issued'
}

export const TERMINAL_STATES: readonly ClickState[] = ['settled', 'rejected', 'expired']

export function isTerminal(state: ClickState): boolean {
  return TERMINAL_STATES.includes(state)
}

export interface RunCounters {
  processed: number
  resolved: number
  awaitingDisclosure: number
  completed: number
  settled: number
  rejected: number
  expired: number
  totalCommission: number
  /** Mean over settled clicks only. Rejections and expiries are exposed as separate counters. */
  perSettledClick: number | null
}

/** The four counters pinned above the feed. One pass, so this is cheap to call every tick. */
export function runCounters(clicks: readonly ClickRecord[], now: number): RunCounters {
  let settled = 0
  let rejected = 0
  let expired = 0
  let awaitingDisclosure = 0
  let totalCommission = 0

  for (const click of clicks) {
    switch (clickState(click, now)) {
      case 'settled':
        settled += 1
        totalCommission += click.commission ?? 0
        break
      case 'rejected':
        rejected += 1
        break
      case 'expired':
        expired += 1
        break
      case 'routed':
        awaitingDisclosure += 1
        break
      case 'issued':
        break
    }
  }

  const completed = settled + rejected + expired
  return {
    processed: clicks.length,
    resolved: completed,
    awaitingDisclosure,
    completed,
    settled,
    rejected,
    expired,
    // Four decimals: the API reports per-click earnings at this precision, so the total should
    // agree with the visible rows without floating-point artifacts.
    totalCommission: Math.round(totalCommission * 10_000) / 10_000,
    perSettledClick: settled === 0 ? null : Math.round((totalCommission / settled) * 10_000) / 10_000,
  }
}

/**
 * An append-and-update log, keyed by click id.
 *
 * A plain array plus a Map index rather than a store library: the UI needs insertion order for its
 * default sort, and the loop needs O(1) update-by-id when a commission arrives.
 */
export class ClickLog {
  private readonly order: ClickRecord[] = []
  private readonly byId = new Map<string, ClickRecord>()

  append(click: ClickRecord): void {
    if (this.byId.has(click.clickId)) {
      throw new Error(
        `ClickLog: click "${click.clickId}" is already in the log. The server issues each id once, ` +
          `so this means the loop recorded the same click twice.`,
      )
    }
    this.order.push(click)
    this.byId.set(click.clickId, click)
  }

  get(clickId: string): ClickRecord | undefined {
    return this.byId.get(clickId)
  }

  has(clickId: string): boolean {
    return this.byId.has(clickId)
  }

  /**
   * Folds a disclosure into an existing row.
   *
   * Returns null when the click is unknown, rather than throwing or inventing a row.
   *
   * An unknown id is NOT a bug, and this is worth spelling out because the obvious guess is that it
   * must be. The commission feed is scoped to the CREDENTIAL PAIR, not to a page load. Reload the
   * client and its in-memory log starts empty while the server quite correctly replays every
   * commission that pair has ever earned, including for clicks a previous page instance issued.
   * There is no row to fold those into, so they are skipped.
   *
   * The caller counts the skips and surfaces them, because silently dropping rows from a feed
   * whose whole purpose is reporting money would be the wrong kind of quiet.
   */
  update(clickId: string, patch: Partial<Omit<ClickRecord, 'clickId'>>): ClickRecord | null {
    const existing = this.byId.get(clickId)
    if (!existing) return null
    Object.assign(existing, patch)
    return existing
  }

  /** Newest first, which is the feed's default ordering. */
  all(): readonly ClickRecord[] {
    return this.order
  }

  get size(): number {
    return this.order.length
  }
}
