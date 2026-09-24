/**
 * Feed filtering, sorting and backoff.
 *
 * The filter tests pin one distinction the UI would otherwise blur: "errors only" means clicks that
 * FAILED, which is not the same as "clicks carrying an error code" — a lapsed click has no code and
 * is still a failure, and a live click that will eventually expire has no code yet and is not.
 */
import { describe, expect, it } from 'vitest'
import { applyFilters, filtersAreEmpty, NO_FILTERS } from './filter'
import { applySort, nextSort, NO_SORT } from './sort'
import { backoffAfterFailure, CLICK_INTERVAL_MS, MAX_BACKOFF_MS } from '../loop/backoff'
import type { ClickRecord } from '../loop/clickLog'

const T0 = 1_700_000_000_000
const EXPIRY = T0 + 120_000

function record(overrides: Partial<ClickRecord> = {}): ClickRecord {
  return {
    clickId: 'clk_default',
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
    ...overrides,
  }
}

const SETTLED = record({
  clickId: 'clk_settled',
  merchantName: 'Best Buy',
  merchantId: 10,
  routedAt: T0,
  routedNetworkId: 'net-a',
  routedNetworkName: 'Network A',
  commission: 0.5713,
  disclosedAt: T0 + 60_000,
})
const REJECTED = record({
  clickId: 'clk_rejected',
  routedAt: T0,
  routedNetworkId: 'net-b',
  routedNetworkName: 'Network B',
  disclosedAt: T0,
  errorCode: 'MERCHANT_NOT_SUPPORTED',
})
const LAPSED = record({ clickId: 'clk_lapsed' })
const LIVE = record({ clickId: 'clk_live', issuedAt: T0 + 110_000, expiresAt: T0 + 230_000 })

const ALL = [SETTLED, REJECTED, LAPSED, LIVE]
const NOW = T0 + 121_000

describe('applyFilters', () => {
  it('returns everything when nothing is filtered', () => {
    expect(applyFilters(ALL, NO_FILTERS, NOW)).toHaveLength(4)
    expect(filtersAreEmpty(NO_FILTERS)).toBe(true)
  })

  it('filters by routed network', () => {
    const rows = applyFilters(ALL, { ...NO_FILTERS, networkId: 'net-a' }, NOW)
    expect(rows.map((r) => r.clickId)).toEqual(['clk_settled'])
  })

  it('filters by derived state, including states never stored', () => {
    expect(applyFilters(ALL, { ...NO_FILTERS, state: 'settled' }, NOW).map((r) => r.clickId)).toEqual([
      'clk_settled',
    ])
    // `expired` is derived from a timestamp, not stored anywhere.
    expect(applyFilters(ALL, { ...NO_FILTERS, state: 'expired' }, NOW).map((r) => r.clickId)).toEqual([
      'clk_lapsed',
    ])
    expect(applyFilters(ALL, { ...NO_FILTERS, state: 'issued' }, NOW).map((r) => r.clickId)).toEqual([
      'clk_live',
    ])
  })

  it('matches a merchant by name substring, case-insensitively, or by exact id', () => {
    expect(applyFilters(ALL, { ...NO_FILTERS, merchant: 'best' }, NOW).map((r) => r.clickId)).toEqual([
      'clk_settled',
    ])
    expect(applyFilters(ALL, { ...NO_FILTERS, merchant: '10' }, NOW).map((r) => r.clickId)).toEqual([
      'clk_settled',
    ])
    // An id match is exact: "4" must not match merchant 42.
    expect(applyFilters(ALL, { ...NO_FILTERS, merchant: '4' }, NOW)).toHaveLength(0)
  })

  it('treats "errors only" as failed, not as carrying an error code', () => {
    const rows = applyFilters(ALL, { ...NO_FILTERS, errorsOnly: true }, NOW)
    // The lapsed click has NO errorCode but is a failure; the live click has none and is not.
    expect(rows.map((r) => r.clickId).sort()).toEqual(['clk_lapsed', 'clk_rejected'])
  })

  it('applies every filter together', () => {
    const rows = applyFilters(
      ALL,
      { networkId: 'net-b', state: 'rejected', merchant: 'Target', errorsOnly: true },
      NOW,
    )
    expect(rows.map((r) => r.clickId)).toEqual(['clk_rejected'])
  })
})

describe('nextSort', () => {
  it('cycles off, ascending, descending, off', () => {
    const first = nextSort(NO_SORT, 'issued')
    expect(first).toEqual({ key: 'issued', dir: 1 })
    const second = nextSort(first, 'issued')
    expect(second).toEqual({ key: 'issued', dir: -1 })
    expect(nextSort(second, 'issued')).toEqual(NO_SORT)
  })

  it('resets to ascending when the column changes', () => {
    expect(nextSort({ key: 'issued', dir: -1 }, 'merchant')).toEqual({ key: 'merchant', dir: 1 })
  })
})

describe('applySort', () => {
  const accessors = {
    commission: (click: ClickRecord) => click.commission,
    merchant: (click: ClickRecord) => click.merchantName,
  }
  const fallback = (click: ClickRecord) => click.clickId

  it('returns a copy in the original order when off', () => {
    const rows = applySort(ALL, NO_SORT, accessors, fallback)
    expect(rows.map((r) => r.clickId)).toEqual(ALL.map((r) => r.clickId))
    expect(rows).not.toBe(ALL)
  })

  it('sorts numbers numerically and puts nulls last ascending', () => {
    const rows = applySort(ALL, { key: 'commission', dir: 1 }, accessors, fallback)
    // The only click with a commission comes first; the three unknowns follow.
    expect(rows[0]?.clickId).toBe('clk_settled')
    expect(rows.slice(1).every((r) => r.commission === null)).toBe(true)
  })

  it('keeps nulls last when descending too, rather than hoisting unknowns to the top', () => {
    const rows = applySort(ALL, { key: 'commission', dir: -1 }, accessors, fallback)
    expect(rows[0]?.clickId).toBe('clk_settled')
  })

  it('compares strings with localeCompare', () => {
    const rows = applySort(ALL, { key: 'merchant', dir: 1 }, accessors, fallback)
    expect(rows[0]?.merchantName).toBe('Best Buy')
  })

  it('breaks ties deterministically, so the table does not flicker', () => {
    // Three rows share merchantName "Target"; without a tie-break their order could change between
    // renders, which reads as the table shuffling itself.
    const once = applySort(ALL, { key: 'merchant', dir: 1 }, accessors, fallback).map((r) => r.clickId)
    const twice = applySort([...ALL].reverse(), { key: 'merchant', dir: 1 }, accessors, fallback).map(
      (r) => r.clickId,
    )
    expect(once).toEqual(twice)
  })

  it('refuses an unsortable key rather than silently returning unsorted rows', () => {
    expect(() => applySort(ALL, { key: 'nope', dir: 1 }, accessors, fallback)).toThrow(/not sortable/)
  })
})

describe('backoffAfterFailure', () => {
  it('honors the server\'s retryAfterSeconds exactly', () => {
    // Computed from the real bucket state, so waiting longer just wastes ration.
    expect(backoffAfterFailure(1, 4)).toBe(4_000)
    expect(backoffAfterFailure(9, 7)).toBe(7_000)
  })

  it('never waits less than a second, so a zero cannot hot-loop', () => {
    expect(backoffAfterFailure(1, 0)).toBe(1_000)
    expect(backoffAfterFailure(1, 0.2)).toBe(1_000)
  })

  it('backs off exponentially when the server said nothing', () => {
    expect(backoffAfterFailure(1, undefined)).toBe(1_000)
    expect(backoffAfterFailure(2, undefined)).toBe(2_000)
    expect(backoffAfterFailure(3, undefined)).toBe(4_000)
  })

  it('caps the backoff so the loop always recovers on its own', () => {
    expect(backoffAfterFailure(20, undefined)).toBe(MAX_BACKOFF_MS)
  })

  it('paces clicks at the sustained rate', () => {
    // 20/min.
    expect(CLICK_INTERVAL_MS).toBe(3_000)
  })

  it('rejects a nonsensical attempt count', () => {
    expect(() => backoffAfterFailure(0, undefined)).toThrow(/at least 1/)
  })
})
