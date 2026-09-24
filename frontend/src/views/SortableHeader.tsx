import type { SortState } from '../lib/sort'

/**
 * A sortable column header.
 *
 * The two triangles are inline SVG, per the design handoff — the only vector art in the client
 * besides the alert icon. Off: both at low opacity. Active: the chosen direction at full.
 */
export function SortableHeader({
  label,
  sortKey,
  sort,
  onSort,
  width,
  align = 'left',
}: {
  label: string
  sortKey: string
  sort: SortState
  onSort: (key: string) => void
  width?: string
  align?: 'left' | 'right'
}) {
  const active = sort.key === sortKey && sort.dir !== 0
  const ascending = active && sort.dir === 1
  const descending = active && sort.dir === -1

  return (
    <th style={{ width, textAlign: align }}>
      <button
        type="button"
        aria-label={`Sort by ${label}`}
        onClick={() => onSort(sortKey)}
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          flexDirection: align === 'right' ? 'row-reverse' : 'row',
          gap: 5,
          background: 'transparent',
          border: 0,
          padding: 0,
          cursor: 'pointer',
          font: 'inherit',
          letterSpacing: 'inherit',
          textTransform: 'inherit',
          color: active ? 'var(--color-accent)' : 'color-mix(in srgb, var(--color-text) 60%, transparent)',
        }}
      >
        {label}
        <svg width="7" height="11" viewBox="0 0 7 11" aria-hidden="true">
          <path d="M3.5 0.6 6.4 4.4H0.6z" fill="currentColor" fillOpacity={ascending ? 1 : active ? 0.22 : 0.3} />
          <path d="M3.5 10.4 0.6 6.6h5.8z" fill="currentColor" fillOpacity={descending ? 1 : active ? 0.22 : 0.3} />
        </svg>
      </button>
    </th>
  )
}
