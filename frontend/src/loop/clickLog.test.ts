/**
 * The click log and the counters above the feed.
 *
 * The counter assertions are the ones worth having: they are what an operator reads to decide
 * whether the run is going well, and the per-click average in particular is easy to get subtly
 * wrong by dividing by the wrong denominator.
 */
import { describe, expect, it } from 'vitest'
import {
  ClickLog,
  clickState,
  decisionSummary,
  isTerminal,
  runCounters,
  type ClickRecord,
  type ClickState,
} from './clickLog'

const T0 = 1_700_000_000_000
const EXPIRY = T0 + 120_000

function record(overrides: Partial<ClickRecord> = {}): ClickRecord {
  return {
    clickId: `clk_${Math.random().toString(16).slice(2, 10)}`,
    merchantId: 42,
    merchantName: 'Target',
    issuedAt: T0,
    expiresAt: EXPIRY,
    routedAt: null,
    routedNetworkId: null,
    routedNetworkName: null,
    commission: null,
    disclosedAt: null,
    errorCode: null,
    decision: null,
    ...overrides,
  }
}

describe('clickState', () => {
  it('is issued while live and unrouted', () => {
    expect(clickState(record(), T0 + 1_000)).toBe('issued')
  })

  it('is routed once routed but before disclosure', () => {
    const click = record({ routedAt: T0 + 5_000, disclosedAt: T0 + 65_000 })
    expect(clickState(click, T0 + 30_000)).toBe('routed')
  })

  it('is settled once the disclosure time is reached', () => {
    const click = record({ routedAt: T0 + 5_000, disclosedAt: T0 + 65_000, commission: 0.45 })
    expect(clickState(click, T0 + 65_000)).toBe('settled')
  })

  it('is rejected when it carries an error code, whatever else is true', () => {
    const spent = record({ routedAt: T0 + 5_000, disclosedAt: T0 + 5_000, errorCode: 'MERCHANT_NOT_SUPPORTED' })
    expect(clickState(spent, T0 + 6_000)).toBe('rejected')

    // A click spent on a too-late routing is REJECTED, not merely expired: the candidate used it.
    const late = record({ routedAt: EXPIRY + 1_000, disclosedAt: EXPIRY + 1_000, errorCode: 'CLICK_EXPIRED' })
    expect(clickState(late, EXPIRY + 2_000)).toBe('rejected')
  })

  it('is expired only when it was never routed and its time has passed', () => {
    expect(clickState(record(), EXPIRY)).toBe('expired')
    // One millisecond earlier it is still live and routable.
    expect(clickState(record(), EXPIRY - 1)).toBe('issued')
  })

  it('marks the right states terminal', () => {
    const terminal: ClickState[] = ['settled', 'rejected', 'expired']
    expect(terminal.every(isTerminal)).toBe(true)
    expect(isTerminal('routed')).toBe(false)
    expect(isTerminal('issued')).toBe(false)
  })
})

describe('runCounters', () => {
  it('counts an empty run without dividing by zero', () => {
    const counters = runCounters([], T0)
    expect(counters).toMatchObject({ processed: 0, completed: 0, totalCommission: 0, perSettledClick: null })
  })

  it('separates awaiting-disclosure from completed', () => {
    const clicks = [
      record({ routedAt: T0, disclosedAt: T0 + 60_000, commission: 0.5 }),
      record({ routedAt: T0, disclosedAt: T0 + 60_000, commission: 0.3 }),
      // Routed, still withheld.
      record({ routedAt: T0 + 50_000, disclosedAt: T0 + 110_000, commission: 0.55 }),
    ]
    const counters = runCounters(clicks, T0 + 60_000)

    expect(counters.processed).toBe(3)
    expect(counters.settled).toBe(2)
    expect(counters.awaitingDisclosure).toBe(1)
    expect(counters.completed).toBe(2)
    // The withheld click's commission must NOT be counted, even though the row holds a number.
    expect(counters.totalCommission).toBe(0.8)
  })

  it('averages over settled clicks only, not over every click', () => {
    const clicks = [
      record({ routedAt: T0, disclosedAt: T0 + 60_000, commission: 0.5 }),
      record({ routedAt: T0, disclosedAt: T0 + 60_000, errorCode: 'MERCHANT_NOT_SUPPORTED' }),
      record(),
    ]
    const counters = runCounters(clicks, T0 + 60_000)

    expect(counters.settled).toBe(1)
    expect(counters.rejected).toBe(1)
    // $0.5000 over one settled click. Folding unrelated outcomes into this metric would mislabel it.
    expect(counters.perSettledClick).toBe(0.5)
  })

  it('rounds money so a floating-point sum does not leak binary artifacts', () => {
    const clicks = [
      record({ routedAt: T0, disclosedAt: T0, commission: 0.1 }),
      record({ routedAt: T0, disclosedAt: T0, commission: 0.2 }),
    ]
    expect(runCounters(clicks, T0).totalCommission).toBe(0.3)
  })

  it('preserves four-decimal CPC precision in totals and averages', () => {
    const clicks = [0.0975, 0.1394, 0.3037].map((commission, index) =>
      record({
        clickId: `clk_${index}`,
        routedAt: T0,
        routedNetworkId: 'net-a',
        commission,
        disclosedAt: T0,
      }),
    )
    expect(runCounters(clicks, T0).totalCommission).toBe(0.5406)
    expect(runCounters(clicks, T0).perSettledClick).toBe(0.1802)
  })

  it('counts a lapsed click as completed but not as settled', () => {
    const counters = runCounters([record()], EXPIRY + 1)
    expect(counters.expired).toBe(1)
    expect(counters.completed).toBe(1)
    expect(counters.settled).toBe(0)
    expect(counters.perSettledClick).toBeNull()
  })
})

describe('decisionSummary', () => {
  function routedTo(networkId: string, modelName: string, exploratory: boolean | null): ClickRecord {
    return record({
      routedAt: T0,
      routedNetworkId: networkId,
      decision: { modelName, rationale: 'because', exploratory },
    })
  }

  it('summarizes an empty run without dividing by zero', () => {
    const summary = decisionSummary([], 'thompson')
    expect(summary).toMatchObject({ routed: 0, decided: 0, exploratory: 0 })
    expect(summary.shareByNetwork.size).toBe(0)
  })

  it('shares out the routed clicks by network, leaving out clicks that never went anywhere', () => {
    const clicks = [
      routedTo('net-a', 'thompson', false),
      routedTo('net-a', 'thompson', false),
      routedTo('net-b', 'thompson', true),
      // Issued, never routed: it did not go to any network, so it is in no network's share.
      record(),
    ]
    const summary = decisionSummary(clicks, 'thompson')

    expect(summary.routed).toBe(3)
    expect(summary.shareByNetwork.get('net-a')).toBeCloseTo(2 / 3)
    expect(summary.shareByNetwork.get('net-b')).toBeCloseTo(1 / 3)
  })

  it('counts exploration for the named model only', () => {
    const clicks = [
      routedTo('net-a', 'thompson', true),
      routedTo('net-a', 'thompson', false),
      // Still a routed click, so it counts toward the shares. But another model decided it, and
      // the random model has no favorite to pass over.
      routedTo('net-b', 'random', null),
    ]

    expect(decisionSummary(clicks, 'thompson')).toMatchObject({ routed: 3, decided: 2, exploratory: 1 })
  })

  it('looks at the most recent window only, so it describes now rather than the whole run', () => {
    const early = Array.from({ length: 50 }, () => routedTo('net-a', 'thompson', true))
    const recent = Array.from({ length: 10 }, () => routedTo('net-b', 'thompson', false))
    const summary = decisionSummary([...early, ...recent], 'thompson', 10)

    expect(summary).toMatchObject({ routed: 10, decided: 10, exploratory: 0 })
    expect(summary.shareByNetwork.get('net-b')).toBe(1)
    expect(summary.shareByNetwork.has('net-a')).toBe(false)
  })
})

describe('ClickLog', () => {
  it('appends and folds a disclosure into the existing row', () => {
    const log = new ClickLog()
    // Routed first: a click cannot settle without having been routed, which is why the loop records
    // the routing before the feed ever reports on it.
    log.append(record({ clickId: 'clk_one', routedAt: T0, routedNetworkId: 'net-truenorth' }))

    log.update('clk_one', { commission: 0.5725, disclosedAt: T0 + 60_000 })

    expect(log.size).toBe(1)
    expect(log.get('clk_one')?.commission).toBe(0.5725)
    expect(clickState(log.get('clk_one')!, T0 + 60_000)).toBe('settled')
  })

  it('refuses to record the same click twice', () => {
    const log = new ClickLog()
    log.append(record({ clickId: 'clk_dupe' }))
    expect(() => log.append(record({ clickId: 'clk_dupe' }))).toThrow(/already in the log/)
  })

  it('reports an unknown click rather than throwing or inventing a row', () => {
    const log = new ClickLog()
    /*
     * This is the reload case, and it is NOT an error. The commission feed is scoped to the
     * credential pair, not to a page load, so a fresh client legitimately receives commissions for
     * clicks a previous instance issued. Returning null lets the loop count and surface those
     * instead of dying on them -- an earlier version threw here, which killed every poll.
     */
    expect(log.update('clk_unknown', { commission: 0.1 })).toBeNull()
    expect(log.has('clk_unknown')).toBe(false)
    expect(log.size).toBe(0)
  })

  it('preserves insertion order, which the feed reverses for newest-first', () => {
    const log = new ClickLog()
    for (const id of ['clk_a', 'clk_b', 'clk_c']) log.append(record({ clickId: id }))
    expect(log.all().map((click) => click.clickId)).toEqual(['clk_a', 'clk_b', 'clk_c'])
  })
})
