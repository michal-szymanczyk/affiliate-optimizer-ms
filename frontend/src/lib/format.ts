/** Formatting helpers shared across the client. */

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

/** Runtime as `14m 03s`, or `1h 04m` past an hour. */
export function duration(ms: number): string {
  const totalSeconds = Math.max(0, Math.floor(ms / 1000))
  const hours = Math.floor(totalSeconds / 3600)
  const minutes = Math.floor((totalSeconds % 3600) / 60)
  const seconds = totalSeconds % 60
  if (hours > 0) return `${hours}h ${String(minutes).padStart(2, '0')}m`
  return `${minutes}m ${String(seconds).padStart(2, '0')}s`
}

/** HH:MM:SS in UTC, for the feed's timestamps. */
export function utcTime(ms: number): string {
  return new Date(ms).toISOString().slice(11, 19)
}

export function count(n: number): string {
  return n.toLocaleString('en-US')
}

/** The last six characters of a click id, which is how the feed labels a row. */
export function shortClickId(clickId: string): string {
  return clickId.slice(-6)
}
