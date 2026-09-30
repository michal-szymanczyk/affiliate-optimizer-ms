/**
 * Formatting helpers shared across the operator UI.
 *
 * TIME COMES FROM THE SERVER. Every response carries `serverNow`, and every age, duration and
 * "running for" here is the difference between two server timestamps. The browser clock is never
 * read: a laptop that is minutes off would otherwise show clicks settling in the future.
 */

/**
 * Money, at two precisions, and the distinction matters.
 *
 * Commissions here are earnings per CLICK, so a single one lives in fractions of a cent: rendering
 * $0.0975 as "$0.10" throws away most of what distinguishes one network from another. Accumulated
 * totals run into whole dollars, where four decimals is just noise.
 *
 *   rate()  per-click commissions, advertised rates, per-click averages
 *   money() running totals, and anything summed over many clicks
 */

/** Accumulated amounts. Two decimals. */
export function money(n: number): string {
  return `$${n.toFixed(2)}`
}

/** Per-click amounts. Four decimals, matching COMMISSION_DECIMALS on the server. */
export function rate(n: number): string {
  return `$${n.toFixed(4)}`
}

/** A signed per-click difference, for gaps and trends: `+$0.0120` / `-$0.0031`. */
export function signedRate(n: number): string {
  const sign = n > 0 ? '+' : n < 0 ? '-' : ''
  return `${sign}$${Math.abs(n).toFixed(4)}`
}

/** Runtime as `14m 03s`, or `1h 04m` past an hour. */
export function duration(ms: number): string {
  const totalSeconds = Math.max(0, Math.floor(ms / 1000))
  const hours = Math.floor(totalSeconds / 3600)
  const minutes = Math.floor((totalSeconds % 3600) / 60)
  const seconds = totalSeconds % 60
  if (hours > 0) return `${hours}h ${String(minutes).padStart(2, '0')}m`
  return `${minutes}m ${String(seconds).padStart(2, '0')}s`
}

/**
 * Milliseconds since the epoch for an ISO 8601 string.
 *
 * The host serialises UTC timestamps with a trailing `Z`. A string without any zone designator is
 * still read as UTC rather than local time, which is what `Date.parse` would otherwise do with it.
 */
export function parseIso(iso: string): number {
  const zoned = /(?:[zZ]|[+-]\d{2}:?\d{2})$/.test(iso) ? iso : `${iso}Z`
  return Date.parse(zoned)
}

/** `to - from` in milliseconds, never negative. Both ISO strings from the server. */
export function elapsedMs(fromIso: string, toIso: string): number {
  return Math.max(0, parseIso(toIso) - parseIso(fromIso))
}

/**
 * A compact age relative to the server's clock: `4s ago`, `61s ago`, `3m 10s ago`, `1h 04m ago`.
 *
 * Seconds up to two minutes, because "61s ago" for a disclosure that arrives a minute after routing
 * is the number an operator is actually comparing against.
 */
export function ago(iso: string | null | undefined, serverNowIso: string): string {
  if (iso === null || iso === undefined) return 'never'
  const seconds = Math.floor(elapsedMs(iso, serverNowIso) / 1000)
  if (seconds < 120) return `${seconds}s ago`
  return `${duration(seconds * 1000)} ago`
}

/** How long until a server timestamp: `in 4s`, or `now` once it has passed. */
export function until(iso: string, serverNowIso: string): string {
  const seconds = Math.ceil(elapsedMs(serverNowIso, iso) / 1000)
  return seconds <= 0 ? 'now' : `in ${seconds}s`
}

/** HH:MM:SS in UTC, for the feed's timestamps. */
export function utcTime(value: number | string): string {
  const ms = typeof value === 'string' ? parseIso(value) : value
  return new Date(ms).toISOString().slice(11, 19)
}

/** `YYYY-MM-DD HH:MM` in UTC, for run history. */
export function utcDateTime(value: number | string): string {
  const ms = typeof value === 'string' ? parseIso(value) : value
  return new Date(ms).toISOString().slice(0, 16).replace('T', ' ')
}

export function count(n: number): string {
  return n.toLocaleString('en-US')
}

/** The last six characters of a click id, which is how the feed labels a row. */
export function shortClickId(clickId: string): string {
  return clickId.slice(-6)
}

/** A percentage with one decimal and a sign, for claim gaps: `-12.5%`. */
export function signedPercent(n: number): string {
  const sign = n > 0 ? '+' : ''
  return `${sign}${n.toFixed(1)}%`
}
