import { useCallback, useEffect, useState } from 'react'
import { operatorApi } from '../api/operator'
import type { ClickRow, ClickState, ModelInfo, NetworkInsight, NetworkScore } from '../api/types'
import { POLL_INTERVAL_MS, usePolling } from '../hooks/usePolling'
import { count, rate, shortClickId, utcTime } from '../lib/format'
import { nextSort, NO_SORT, sortParams, type SortState } from '../lib/sort'
import { AlertIcon } from './AlertIcon'
import { InlineError } from './Notice'
import { SortableHeader } from './SortableHeader'
import { CELL, KICKER, MUTED } from './style'

/** One page of the feed. The host caps a page at 500; 40 is what fits a screen. */
const PAGE_SIZE = 40

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
  { value: 'issued', label: 'issued' },
]

interface FeedFilters {
  networkId: string
  state: ClickState | 'all'
  merchant: string
  modelName: string
  errorsOnly: boolean
}

const NO_FILTERS: FeedFilters = {
  networkId: 'all',
  state: 'all',
  merchant: '',
  modelName: 'all',
  errorsOnly: false,
}

function filtersAreEmpty(filters: FeedFilters): boolean {
  return (
    filters.networkId === NO_FILTERS.networkId &&
    filters.state === NO_FILTERS.state &&
    filters.merchant.trim() === NO_FILTERS.merchant &&
    filters.modelName === NO_FILTERS.modelName &&
    filters.errorsOnly === NO_FILTERS.errorsOnly
  )
}

/**
 * The commission cell.
 *
 * Four distinct readings, and the distinction matters: a settled click shows what it earned, a click
 * spent on a failed routing reads "spent", one that lapsed unrouted reads "lapsed", and one still
 * awaiting a decision or a disclosure reads "—". Three of those are "no money" but they are not the
 * same fact.
 */
function CommissionCell({ click }: { click: ClickRow }) {
  if (click.state === 'settled' && click.commission !== null) {
    return <span style={{ color: 'var(--color-text)' }}>{rate(click.commission)}</span>
  }
  const label = click.state === 'rejected' ? 'spent' : click.state === 'expired' ? 'lapsed' : '—'
  return <span style={{ color: MUTED(35) }}>{label}</span>
}

function isNetworkScore(value: unknown): value is NetworkScore {
  if (typeof value !== 'object' || value === null) return false
  const score = value as Partial<NetworkScore>
  return typeof score.networkId === 'string' && typeof score.estimate === 'number'
}

/** The model's scores, as stored; `null` when no decision was recorded, `'unreadable'` when malformed. */
function parseScores(json: string | null): NetworkScore[] | 'unreadable' | null {
  if (json === null) return null
  try {
    const parsed: unknown = JSON.parse(json)
    return Array.isArray(parsed) ? parsed.filter(isNetworkScore) : 'unreadable'
  } catch {
    return 'unreadable'
  }
}

/**
 * The "why" behind one decision: the rationale the model recorded and the score it gave every
 * eligible network. This is the row an operator opens when a routing looks wrong.
 */
function WhyRow({ click, networkNames }: { click: ClickRow; networkNames: ReadonlyMap<string, string> }) {
  const scores = parseScores(click.scoresJson)
  const nameOf = (networkId: string) => networkNames.get(networkId) ?? networkId

  return (
    <tr data-why-row={click.clickId}>
      <td colSpan={8} style={{ padding: '10px 18px 16px', background: MUTED(3) }}>
        <div style={{ display: 'grid', gap: 10, fontSize: 13 }}>
          <div style={KICKER}>Decision</div>
          <div>
            {click.decidedNetworkId === null ? (
              <span style={{ color: MUTED(55) }}>
                No decision was recorded for this click
                {click.modelName !== null ? ` by ${click.modelName}` : ''}.
              </span>
            ) : (
              <>
                <span style={{ fontWeight: 500 }}>{click.modelName ?? 'unknown model'}</span> chose{' '}
                <span style={{ fontWeight: 500 }}>{nameOf(click.decidedNetworkId)}</span>
                {click.rationale !== null ? <span style={{ color: MUTED(70) }}> — {click.rationale}</span> : null}
              </>
            )}
            {click.decidedNetworkId !== null &&
            click.routedNetworkId !== null &&
            click.decidedNetworkId !== click.routedNetworkId ? (
              <div style={{ color: 'var(--color-accent-700)', marginTop: 4 }}>
                The feed reports this click routed to {nameOf(click.routedNetworkId)}, not the decided network.
              </div>
            ) : null}
          </div>

          {scores === 'unreadable' ? (
            <div style={{ color: 'var(--color-accent-700)' }}>The stored scores could not be read.</div>
          ) : scores !== null && scores.length > 0 ? (
            <table className="table" style={{ fontSize: 12, width: 'auto', minWidth: 520 }}>
              <thead>
                <tr>
                  <th>Network</th>
                  <th style={{ textAlign: 'right' }}>Estimate</th>
                  <th>Basis</th>
                  <th style={{ textAlign: 'right' }}>Samples</th>
                </tr>
              </thead>
              <tbody>
                {scores.map((score) => {
                  const chosen = score.networkId === click.decidedNetworkId
                  return (
                    <tr key={score.networkId}>
                      <td style={{ fontWeight: chosen ? 500 : 400 }}>
                        {nameOf(score.networkId)}
                        {chosen ? (
                          <span className="tag tag-outline" style={{ marginLeft: 8 }}>
                            chosen
                          </span>
                        ) : null}
                      </td>
                      <td className="tnum" style={{ textAlign: 'right' }}>
                        {rate(score.estimate)}
                      </td>
                      <td style={{ color: MUTED(70) }}>{score.basis}</td>
                      <td className="tnum" style={{ textAlign: 'right' }}>
                        {count(score.samples)}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          ) : (
            <div style={{ color: MUTED(55) }}>No per-network scores were stored for this decision.</div>
          )}
        </div>
      </td>
    </tr>
  )
}

/**
 * The click feed: one page of the host's click store, filtered, sorted and paged on the server.
 *
 * The original client filtered in memory because a run lived and died with a browser tab. Runs now
 * outlive the tab and history accumulates, so every control here is a query parameter and the table
 * shows exactly what `GET /api/clicks/getall` returned -- nothing is derived locally, including the
 * state of each row, which the host derives at server time.
 */
export function ClickFeed({
  runId,
  networks,
  models,
}: {
  /** The run in scope; undefined shows every run's clicks. */
  runId: number | undefined
  /** The run's network catalogue, for the filter and for naming scored networks. */
  networks: readonly NetworkInsight[]
  models: readonly ModelInfo[]
}) {
  const [filters, setFilters] = useState<FeedFilters>(NO_FILTERS)
  const [sort, setSort] = useState<SortState>(NO_SORT)
  const [pageIndex, setPageIndex] = useState(0)
  const [openWhy, setOpenWhy] = useState<string | null>(null)

  // A new run scope starts from the first page; the filters and sort are the operator's and stay.
  useEffect(() => setPageIndex(0), [runId])

  const fetcher = useCallback(
    () =>
      operatorApi.getClicks(
        {
          runId,
          networkId: filters.networkId === 'all' ? undefined : filters.networkId,
          state: filters.state === 'all' ? undefined : filters.state,
          merchant: filters.merchant.trim() === '' ? undefined : filters.merchant.trim(),
          errorsOnly: filters.errorsOnly ? true : undefined,
          modelName: filters.modelName === 'all' ? undefined : filters.modelName,
        },
        { pageIndex, pageSize: PAGE_SIZE, ...sortParams(sort) },
      ),
    [runId, filters, sort, pageIndex],
  )
  const { data, error } = usePolling(fetcher, POLL_INTERVAL_MS)

  const page = data?.page ?? null
  const total = page?.totalItems ?? 0
  const rows = page?.items ?? []
  const pageCount = Math.max(1, Math.ceil(total / PAGE_SIZE))

  // Filters can shrink the result under the current page; fall back to the last page that exists.
  useEffect(() => {
    if (page !== null && page.items.length === 0 && pageIndex > 0 && total <= pageIndex * PAGE_SIZE) {
      setPageIndex(Math.max(0, pageCount - 1))
    }
  }, [page, pageIndex, total, pageCount])

  const updateFilters = (next: FeedFilters) => {
    setFilters(next)
    setPageIndex(0)
  }
  const cycle = (key: string) => {
    setSort(nextSort(sort, key))
    setPageIndex(0)
  }

  const networkNames = new Map(networks.map((network) => [network.networkId, network.name]))
  const from = total === 0 ? 0 : pageIndex * PAGE_SIZE + 1
  const to = pageIndex * PAGE_SIZE + rows.length

  const ordering =
    sort.key === null || sort.dir === 0 ? 'newest first' : `sorted by ${sort.key} ${sort.dir === 1 ? 'ascending' : 'descending'}`

  return (
    <>
      <div style={{ marginTop: 26, marginBottom: 20 }}>
        <h3 style={{ margin: '0 0 6px' }}>Click feed</h3>
        <div style={{ fontSize: 13, color: MUTED(55) }}>
          {page === null ? 'Reading the click store…' : `${count(total)} clicks match · ${ordering}`}
          {runId !== undefined ? ` · run #${runId}` : ''}
        </div>
      </div>

      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 14, alignItems: 'flex-end', marginBottom: 22 }}>
        <div className="field" style={{ width: 210 }}>
          <label htmlFor="f-network">Routed network</label>
          <select
            id="f-network"
            className="input"
            value={filters.networkId}
            onChange={(event) => updateFilters({ ...filters, networkId: event.target.value })}
          >
            <option value="all">All networks</option>
            {networks.map((network) => (
              <option key={network.networkId} value={network.networkId}>
                {network.name}
              </option>
            ))}
          </select>
        </div>

        <div className="field" style={{ width: 150 }}>
          <label htmlFor="f-state">State</label>
          <select
            id="f-state"
            className="input"
            value={filters.state}
            onChange={(event) => updateFilters({ ...filters, state: event.target.value as ClickState | 'all' })}
          >
            {STATE_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </div>

        <div className="field" style={{ width: 180 }}>
          <label htmlFor="f-merchant">Merchant</label>
          <input
            id="f-merchant"
            className="input"
            placeholder="name or id"
            value={filters.merchant}
            onChange={(event) => updateFilters({ ...filters, merchant: event.target.value })}
          />
        </div>

        <div className="field" style={{ width: 170 }}>
          <label htmlFor="f-model">Model</label>
          <select
            id="f-model"
            className="input"
            value={filters.modelName}
            onChange={(event) => updateFilters({ ...filters, modelName: event.target.value })}
          >
            <option value="all">All models</option>
            {models.map((model) => (
              <option key={model.name} value={model.name}>
                {model.name}
              </option>
            ))}
          </select>
        </div>

        <div className="field">
          <span className="field-label" id="f-errors-label">
            Errors
          </span>
          <div className="seg" role="radiogroup" aria-labelledby="f-errors-label">
            <label className="seg-opt">
              <input
                type="radio"
                name="errors"
                checked={!filters.errorsOnly}
                onChange={() => updateFilters({ ...filters, errorsOnly: false })}
              />
              All rows
            </label>
            <label className="seg-opt">
              <input
                type="radio"
                name="errors"
                checked={filters.errorsOnly}
                onChange={() => updateFilters({ ...filters, errorsOnly: true })}
              />
              Errors only
            </label>
          </div>
        </div>

        <button type="button" className="btn btn-secondary" onClick={() => updateFilters(NO_FILTERS)}>
          Clear
        </button>
      </div>

      <InlineError error={error} prefix="The feed could not refresh" />

      <table className="table">
        <thead>
          <tr>
            <SortableHeader label="Click" sortKey="clickId" sort={sort} onSort={cycle} width="8%" />
            <SortableHeader label="Issued" sortKey="issuedAt" sort={sort} onSort={cycle} width="9%" />
            <SortableHeader label="Merchant" sortKey="merchantName" sort={sort} onSort={cycle} width="19%" />
            <SortableHeader label="Model" sortKey="modelName" sort={sort} onSort={cycle} width="12%" />
            <SortableHeader label="Routed to" sortKey="routedNetworkName" sort={sort} onSort={cycle} width="17%" />
            <SortableHeader label="State" sortKey="state" sort={sort} onSort={cycle} width="15%" />
            <SortableHeader
              label="Commission"
              sortKey="commission"
              sort={sort}
              onSort={cycle}
              width="12%"
              align="right"
            />
            <th style={{ width: '8%', textAlign: 'right' }}>Why</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((click) => {
            const showError = click.errorCode !== null
            const open = openWhy === click.clickId
            return [
              <tr key={click.clickId} data-click-id={click.clickId} data-click-state={click.state}>
                <td className="tnum" style={{ ...CELL, color: MUTED(60) }}>
                  {shortClickId(click.clickId)}
                </td>
                <td className="tnum" style={{ ...CELL, color: MUTED(60) }}>
                  {utcTime(click.issuedAt)}
                </td>
                <td style={CELL}>
                  {click.merchantName}
                  <span style={{ fontSize: 11, color: MUTED(40) }}> · id {click.merchantId}</span>
                </td>
                <td style={CELL}>{click.modelName ?? <span style={{ color: MUTED(35) }}>—</span>}</td>
                <td style={CELL}>{click.routedNetworkName ?? <span style={{ color: MUTED(35) }}>—</span>}</td>
                <td style={CELL}>
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 4, alignItems: 'flex-start' }}>
                    <span className={STATE_TAG[click.state]}>{click.state}</span>
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
                    {click.routeUnconfirmed ? (
                      <span
                        className="tag tag-outline"
                        title="The route response was lost. The commission feed will reconcile this click if the server accepted it."
                      >
                        unconfirmed
                      </span>
                    ) : null}
                  </div>
                </td>
                <td className="tnum" style={{ ...CELL, textAlign: 'right' }}>
                  <CommissionCell click={click} />
                </td>
                <td style={{ ...CELL, textAlign: 'right' }}>
                  <button
                    type="button"
                    className="btn btn-ghost"
                    aria-expanded={open}
                    aria-label={`Why ${shortClickId(click.clickId)}`}
                    onClick={() => setOpenWhy(open ? null : click.clickId)}
                  >
                    {open ? 'hide' : 'why'}
                  </button>
                </td>
              </tr>,
              open ? <WhyRow key={`${click.clickId}-why`} click={click} networkNames={networkNames} /> : null,
            ]
          })}
        </tbody>
      </table>

      {rows.length === 0 ? (
        <div style={{ padding: '18px 0', fontSize: 14, color: MUTED(45) }}>
          {page === null
            ? ''
            : total === 0 && filtersAreEmpty(filters)
              ? 'No clicks yet. The host issues one every three seconds while a run is running.'
              : 'No clicks match these filters.'}
        </div>
      ) : (
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 14,
            fontSize: 12,
            color: MUTED(45),
            marginTop: 14,
          }}
        >
          <span data-feed-range="">
            Showing {count(from)}–{count(to)} of {count(total)}
          </span>
          <span>· Commissions are disclosed one minute after routing.</span>
          <span style={{ marginLeft: 'auto', display: 'flex', gap: 6, alignItems: 'center' }}>
            <button
              type="button"
              className="btn btn-secondary"
              disabled={pageIndex === 0}
              onClick={() => setPageIndex((index) => Math.max(0, index - 1))}
            >
              Prev
            </button>
            <span className="tnum">
              page {pageIndex + 1} of {pageCount}
            </span>
            <button
              type="button"
              className="btn btn-secondary"
              disabled={to >= total}
              onClick={() => setPageIndex((index) => index + 1)}
            >
              Next
            </button>
          </span>
        </div>
      )}
    </>
  )
}
