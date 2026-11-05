import { count, rate, shortClickId, utcTime } from '../lib/format'
import { applyFilters, filtersAreEmpty, NO_FILTERS, type Filters } from '../lib/filter'
import { applySort, nextSort, type SortState } from '../lib/sort'
import {
  clickState,
  isTerminal,
  type ClickRecord,
  type ClickState,
  type DecisionRecord,
} from '../loop/clickLog'
import type { Network } from '../api/client'
import { AlertIcon } from './AlertIcon'
import { SortableHeader } from './SortableHeader'

/** The feed caps at 40 rows; the footer says what was left out. */
const MAX_ROWS = 40

const STATE_TAG: Record<ClickState, string> = {
  settled: 'tag tag-accent',
  routed: 'tag tag-outline',
  rejected: 'tag tag-neutral',
  expired: 'tag tag-neutral',
  issued: 'tag tag-neutral',
}

const STATE_OPTIONS: { value: ClickState | 'all'; label: string }[] = [
  { value: 'all', label: 'All states' },
  { value: 'routed', label: 'routed' },
  { value: 'settled', label: 'settled' },
  { value: 'rejected', label: 'rejected' },
  { value: 'expired', label: 'expired' },
]

const MUTED = (percent: number) => `color-mix(in srgb, var(--color-text) ${percent}%, transparent)`

/**
 * The commission cell.
 *
 * Three distinct readings, and the distinction matters: a settled click shows what it earned, a
 * click spent on a failed routing reads "spent", and one still awaiting disclosure reads "—". The
 * last two are both "no money" but they are not the same fact.
 */
function CommissionCell({ click, state }: { click: ClickRecord; state: ClickState }) {
  if (state === 'settled' && click.commission !== null) {
    return <span style={{ color: 'var(--color-text)' }}>{rate(click.commission)}</span>
  }
  const spent = isTerminal(state) && state !== 'settled'
  return <span style={{ color: MUTED(35) }}>{spent ? 'spent' : '—'}</span>
}

/**
 * Why the click went where it did: which model decided, whether it was playing its favorite or
 * exploring, and its own account of the choice.
 *
 * The model is named on every row because the active model can be switched mid-run, so one feed
 * can hold decisions from several. A model with no notion of a favorite shows its name alone.
 */
function DecisionCell({ decision }: { decision: DecisionRecord | null }) {
  if (decision === null) return <span style={{ color: MUTED(35) }}>—</span>
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 4, alignItems: 'flex-start' }}>
      <div style={{ display: 'flex', gap: 6 }}>
        <span className="tag tag-neutral">{decision.modelName}</span>
        {decision.exploratory === null ? null : (
          <span className={decision.exploratory ? 'tag tag-outline' : 'tag tag-accent'}>
            {decision.exploratory ? 'exploring' : 'favorite'}
          </span>
        )}
      </div>
      <span style={{ fontSize: 11, color: MUTED(55) }}>{decision.rationale}</span>
    </div>
  )
}

export function ClickFeed({
  clicks,
  networks,
  now,
  filters,
  onFilters,
  sort,
  onSort,
}: {
  clicks: readonly ClickRecord[]
  networks: readonly Network[]
  now: number
  filters: Filters
  onFilters: (filters: Filters) => void
  sort: SortState
  onSort: (sort: SortState) => void
}) {
  const matching = applyFilters(clicks, filters, now)

  const sorted = applySort(
    matching,
    sort,
    {
      click: (click) => click.clickId,
      issued: (click) => click.issuedAt,
      merchant: (click) => click.merchantName,
      network: (click) => click.routedNetworkName,
      state: (click) => clickState(click, now),
      commission: (click) => click.commission,
    },
    (click) => click.clickId,
  )

  // Default ordering is newest first, which the log does not provide on its own (it appends).
  const ordered = sort.key === null || sort.dir === 0 ? [...sorted].reverse() : sorted
  const rows = ordered.slice(0, MAX_ROWS)

  const settledCount = matching.filter((click) => clickState(click, now) === 'settled').length
  const errorCount = matching.filter((click) => {
    const state = clickState(click, now)
    return isTerminal(state) && state !== 'settled'
  }).length

  const cycle = (key: string) => onSort(nextSort(sort, key))

  return (
    <>
      <div style={{ marginTop: 26, marginBottom: 20 }}>
        <h3 style={{ margin: '0 0 6px' }}>Click feed</h3>
        <div style={{ fontSize: 13, color: MUTED(55) }}>
          {count(matching.length)} clicks in view · {count(settledCount)} settled · {count(errorCount)} with
          errors
        </div>
      </div>

      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 14, alignItems: 'flex-end', marginBottom: 22 }}>
        <div className="field" style={{ width: 230 }}>
          <label htmlFor="f-network">Routed network</label>
          <select
            id="f-network"
            className="input"
            value={filters.networkId}
            onChange={(event) => onFilters({ ...filters, networkId: event.target.value })}
          >
            <option value="all">All networks</option>
            {networks.map((network) => (
              <option key={network.id} value={network.id}>
                {network.name}
              </option>
            ))}
          </select>
        </div>

        <div className="field" style={{ width: 180 }}>
          <label htmlFor="f-state">State</label>
          <select
            id="f-state"
            className="input"
            value={filters.state}
            onChange={(event) => onFilters({ ...filters, state: event.target.value as ClickState | 'all' })}
          >
            {STATE_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </div>

        <div className="field" style={{ width: 200 }}>
          <label htmlFor="f-merchant">Merchant</label>
          <input
            id="f-merchant"
            className="input"
            placeholder="name or id"
            value={filters.merchant}
            onChange={(event) => onFilters({ ...filters, merchant: event.target.value })}
          />
        </div>

        <div className="field">
          <span className="field-label" id="f-errors-label">
            Errors
          </span>
          <div className="seg" role="radiogroup" aria-labeledby="f-errors-label">
            <label className="seg-opt">
              <input
                type="radio"
                name="errors"
                checked={!filters.errorsOnly}
                onChange={() => onFilters({ ...filters, errorsOnly: false })}
              />
              All rows
            </label>
            <label className="seg-opt">
              <input
                type="radio"
                name="errors"
                checked={filters.errorsOnly}
                onChange={() => onFilters({ ...filters, errorsOnly: true })}
              />
              Errors only
            </label>
          </div>
        </div>

        <button type="button" className="btn btn-secondary" onClick={() => onFilters(NO_FILTERS)}>
          Clear
        </button>
      </div>

      <table className="table">
        <thead>
          <tr>
            <SortableHeader label="Click" sortKey="click" sort={sort} onSort={cycle} width="8%" />
            <SortableHeader label="Issued" sortKey="issued" sort={sort} onSort={cycle} width="9%" />
            <SortableHeader label="Merchant" sortKey="merchant" sort={sort} onSort={cycle} width="17%" />
            <SortableHeader label="Routed to" sortKey="network" sort={sort} onSort={cycle} width="16%" />
            <SortableHeader label="State" sortKey="state" sort={sort} onSort={cycle} width="11%" />
            <SortableHeader
              label="Commission"
              sortKey="commission"
              sort={sort}
              onSort={cycle}
              width="11%"
              align="right"
            />
            {/* Last on purpose: the end-to-end suite reads the first six cells by position. */}
            <th style={{ width: '28%', paddingLeft: 24 }}>Why</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((click) => {
            const state = clickState(click, now)
            // A lapsed, unrouted click derives to `expired` without a server error code.
            // CLICK_EXPIRED exists only when a route was actually attempted after the deadline.
            const showError = isTerminal(state) && click.errorCode !== null
            return (
              <tr key={click.clickId}>
                <td className="tnum" style={{ padding: '12px var(--space-2)', color: MUTED(60) }}>
                  {shortClickId(click.clickId)}
                </td>
                <td className="tnum" style={{ padding: '12px var(--space-2)', color: MUTED(60) }}>
                  {utcTime(click.issuedAt)}
                </td>
                <td style={{ padding: '12px var(--space-2)' }}>
                  {click.merchantName}
                  <span style={{ fontSize: 11, color: MUTED(40) }}> · id {click.merchantId}</span>
                </td>
                <td style={{ padding: '12px var(--space-2)' }}>{click.routedNetworkName ?? '—'}</td>
                <td style={{ padding: '12px var(--space-2)' }}>
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 4, alignItems: 'flex-start' }}>
                    <span className={STATE_TAG[state]}>{state}</span>
                    {showError ? (
                      <span
                        style={{
                          display: 'flex',
                          alignItems: 'center',
                          gap: 5,
                          fontSize: 11,
                          letterSpacing: '.02em',
                          color: 'var(--color-accent-700)',
                        }}
                      >
                        <AlertIcon size={12} />
                        {click.errorCode}
                      </span>
                    ) : null}
                  </div>
                </td>
                <td className="tnum" style={{ padding: '12px var(--space-2)', textAlign: 'right' }}>
                  <CommissionCell click={click} state={state} />
                </td>
                <td style={{ padding: '12px var(--space-2) 12px 24px' }}>
                  <DecisionCell decision={click.decision} />
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>

      {rows.length === 0 ? (
        <div style={{ padding: '18px 0', fontSize: 14, color: MUTED(45) }}>
          {clicks.length === 0
            ? 'No clicks yet. The loop pulls one every three seconds.'
            : filtersAreEmpty(filters)
              ? 'No clicks yet.'
              : 'No clicks match these filters.'}
        </div>
      ) : (
        <div style={{ fontSize: 12, color: MUTED(45), marginTop: 14 }}>
          {matching.length > rows.length
            ? `Showing the most recent ${rows.length} of ${count(matching.length)} matching clicks. `
            : `Showing all ${count(rows.length)} matching clicks. `}
          Commissions are disclosed one minute after routing.
        </div>
      )}
    </>
  )
}
