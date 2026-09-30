/**
 * Formatting and sort state.
 *
 * Filtering, sorting and backoff used to be tested here; they now run in the .NET host and are
 * tested there. What is left is what the browser still owns: how a number or a server timestamp
 * turns into text, and how a header click cycles.
 */
import { describe, expect, it } from 'vitest'
import {
  ago,
  count,
  duration,
  elapsedMs,
  money,
  parseIso,
  rate,
  shortClickId,
  signedPercent,
  signedRate,
  until,
  utcDateTime,
  utcTime,
} from './format'
import { nextSort, NO_SORT, sortParams } from './sort'

const NOW = '2026-09-28T12:00:00.000Z'

describe('money and rate', () => {
  it('renders totals at two decimals and per-click amounts at four', () => {
    // $0.0975 per click is real information; "$0.10" would erase it.
    expect(rate(0.0975)).toBe('$0.0975')
    expect(money(12.3456)).toBe('$12.35')
    expect(money(0)).toBe('$0.00')
  })

  it('signs differences', () => {
    expect(signedRate(0.012)).toBe('+$0.0120')
    expect(signedRate(-0.0031)).toBe('-$0.0031')
    expect(signedRate(0)).toBe('$0.0000')
    expect(signedPercent(-12.49)).toBe('-12.5%')
    expect(signedPercent(3)).toBe('+3.0%')
  })
})

describe('duration', () => {
  it('formats minutes and seconds, then hours and minutes', () => {
    expect(duration(0)).toBe('0m 00s')
    expect(duration(14 * 60_000 + 3_000)).toBe('14m 03s')
    expect(duration(64 * 60_000)).toBe('1h 04m')
  })

  it('never goes negative', () => {
    expect(duration(-5_000)).toBe('0m 00s')
  })
})

describe('server timestamps', () => {
  it('parses ISO strings and reads a missing zone as UTC', () => {
    expect(parseIso('2026-09-28T12:00:00.000Z')).toBe(Date.UTC(2026, 8, 28, 12))
    // The host always sends a Z; if one ever went missing, local time would be the wrong guess.
    expect(parseIso('2026-09-28T12:00:00')).toBe(Date.UTC(2026, 8, 28, 12))
    expect(parseIso('2026-09-28T13:00:00+01:00')).toBe(Date.UTC(2026, 8, 28, 12))
  })

  it('measures elapsed time between two server timestamps, never negative', () => {
    expect(elapsedMs('2026-09-28T11:59:00.000Z', NOW)).toBe(60_000)
    expect(elapsedMs(NOW, '2026-09-28T11:59:00.000Z')).toBe(0)
  })

  it('renders ages in seconds up to two minutes, then as a duration', () => {
    expect(ago('2026-09-28T11:59:56.000Z', NOW)).toBe('4s ago')
    expect(ago('2026-09-28T11:58:59.000Z', NOW)).toBe('61s ago')
    expect(ago('2026-09-28T11:56:50.000Z', NOW)).toBe('3m 10s ago')
    expect(ago('2026-09-28T10:56:00.000Z', NOW)).toBe('1h 04m ago')
    expect(ago(null, NOW)).toBe('never')
  })

  it('renders time until a retry', () => {
    expect(until('2026-09-28T12:00:04.000Z', NOW)).toBe('in 4s')
    expect(until('2026-09-28T11:59:00.000Z', NOW)).toBe('now')
  })

  it('formats UTC clock times and date-times', () => {
    expect(utcTime(NOW)).toBe('12:00:00')
    expect(utcTime(Date.UTC(2026, 8, 28, 7, 5, 9))).toBe('07:05:09')
    expect(utcDateTime(NOW)).toBe('2026-09-28 12:00')
  })
})

describe('count and shortClickId', () => {
  it('groups thousands and keeps the tail of a click id', () => {
    expect(count(12_345)).toBe('12,345')
    expect(shortClickId('clk_0123456789abcdef')).toBe('abcdef')
  })
})

describe('nextSort', () => {
  it('cycles off, ascending, descending, off', () => {
    const first = nextSort(NO_SORT, 'issuedAt')
    expect(first).toEqual({ key: 'issuedAt', dir: 1 })
    const second = nextSort(first, 'issuedAt')
    expect(second).toEqual({ key: 'issuedAt', dir: -1 })
    expect(nextSort(second, 'issuedAt')).toEqual(NO_SORT)
  })

  it('resets to ascending when the column changes', () => {
    expect(nextSort({ key: 'issuedAt', dir: -1 }, 'merchantName')).toEqual({ key: 'merchantName', dir: 1 })
  })

  it('translates to list-endpoint parameters, sending nothing when off', () => {
    expect(sortParams(NO_SORT)).toEqual({})
    expect(sortParams({ key: 'commission', dir: 1 })).toEqual({ sortBy: 'commission', sortDirection: 'asc' })
    expect(sortParams({ key: 'commission', dir: -1 })).toEqual({ sortBy: 'commission', sortDirection: 'desc' })
  })
})
