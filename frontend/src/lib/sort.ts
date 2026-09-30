/**
 * Sort state for the feed table.
 *
 * Clicking a header cycles off → ascending → descending → off; switching header resets to
 * ascending. The sorting itself happens on the server now (`sortBy` / `sortDirection` on the list
 * endpoints), so this module only owns the cycle and its translation to query parameters.
 */
import type { SortDirection } from '../api/types'

export interface SortState {
  key: string | null
  dir: -1 | 0 | 1
}

export const NO_SORT: SortState = { key: null, dir: 0 }

export function nextSort(current: SortState, key: string): SortState {
  if (current.key !== key) return { key, dir: 1 }
  if (current.dir === 1) return { key, dir: -1 }
  if (current.dir === -1) return NO_SORT
  return { key, dir: 1 }
}

/**
 * The list-endpoint parameters for a sort state. "Off" sends nothing, which lets the server apply
 * its own default (newest first for clicks and runs).
 */
export function sortParams(sort: SortState): { sortBy?: string; sortDirection?: SortDirection } {
  if (sort.key === null || sort.dir === 0) return {}
  return { sortBy: sort.key, sortDirection: sort.dir === 1 ? 'asc' : 'desc' }
}
