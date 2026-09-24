/**
 * Sorting for the feed table.
 *
 * Clicking a header cycles off → ascending → descending → off; switching header resets to
 * ascending. Nulls sort last ascending, which is what "no commission yet" should do — a click still
 * awaiting disclosure is not worth less than one that earned nothing, it is simply unknown.
 */

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

type Sortable = string | number | null | undefined

/**
 * Sorts a copy. Strings compare with `localeCompare`, numbers numerically, nulls last ascending.
 *
 * `fallback` breaks ties so the order is total — without it, two rows with equal commissions would
 * swap places on every re-render, which reads as the table flickering.
 */
export function applySort<T>(
  rows: readonly T[],
  sort: SortState,
  accessors: Record<string, (row: T) => Sortable>,
  fallback: (row: T) => Sortable,
): T[] {
  const { key, dir } = sort
  if (key === null || dir === 0) return [...rows]

  const accessor = accessors[key]
  if (!accessor) {
    throw new Error(
      `applySort: "${key}" is not sortable. Sortable keys: ${Object.keys(accessors).join(', ')}.`,
    )
  }

  return [...rows].sort((a, b) => {
    const compared = compare(accessor(a), accessor(b), dir)
    if (compared !== 0) return compared
    return compare(fallback(a), fallback(b), dir)
  })
}

function compare(a: Sortable, b: Sortable, dir: -1 | 1): number {
  const aMissing = a === null || a === undefined
  const bMissing = b === null || b === undefined
  // Nulls last ASCENDING, which means last regardless of direction being flipped below.
  if (aMissing && bMissing) return 0
  if (aMissing) return 1
  if (bMissing) return -1

  if (typeof a === 'string' && typeof b === 'string') return a.localeCompare(b) * dir
  return (Number(a) - Number(b)) * dir
}
