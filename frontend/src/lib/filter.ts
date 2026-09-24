/**
 * Feed filters.
 *
 * Applied in memory, over the client's own click log. That is fine here and not the same call the
 * server's admin console had to make: this log is one client's run, bounded by 20 clicks a minute,
 * and it is already in the browser. There is nothing to page.
 */
import { clickState, isTerminal, type ClickRecord, type ClickState } from '../loop/clickLog'

export interface Filters {
  networkId: string
  state: ClickState | 'all'
  merchant: string
  errorsOnly: boolean
}

export const NO_FILTERS: Filters = {
  networkId: 'all',
  state: 'all',
  merchant: '',
  errorsOnly: false,
}

export function filtersAreEmpty(filters: Filters): boolean {
  return (
    filters.networkId === NO_FILTERS.networkId &&
    filters.state === NO_FILTERS.state &&
    filters.merchant === NO_FILTERS.merchant &&
    filters.errorsOnly === NO_FILTERS.errorsOnly
  )
}

/** All four filters apply together (AND). */
export function applyFilters(
  clicks: readonly ClickRecord[],
  filters: Filters,
  now: number,
): ClickRecord[] {
  const merchantQuery = filters.merchant.trim().toLowerCase()

  return clicks.filter((click) => {
    if (filters.networkId !== 'all' && click.routedNetworkId !== filters.networkId) return false

    const state = clickState(click, now)
    if (filters.state !== 'all' && state !== filters.state) return false

    /*
     * "Errors only" means a click that FAILED, which is narrower than "has an errorCode": a click
     * shows no code until it is terminal, and a lapsed click has no code at all yet is a failure.
     */
    if (filters.errorsOnly && !(isTerminal(state) && state !== 'settled')) return false

    if (merchantQuery.length > 0) {
      const byName = click.merchantName.toLowerCase().includes(merchantQuery)
      const byId = String(click.merchantId) === merchantQuery
      if (!byName && !byId) return false
    }

    return true
  })
}
