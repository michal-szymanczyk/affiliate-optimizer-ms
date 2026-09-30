import type { RunStatus, RunsResponse } from '../api/types'
import { count, duration, elapsedMs, money, rate, utcDateTime } from '../lib/format'
import { InlineError } from './Notice'
import { CELL, MUTED } from './style'

/**
 * Run history: every run this host has recorded, newest first, with its totals and provenance.
 *
 * This is where "current" and "historical" meet. Picking a run scopes the feed, the network insight
 * and the comparison to it, so an operator can look at last night's run with the same panels they use
 * for the live one -- and the provenance columns (models, window, credential, build) say whether two
 * runs are even comparable.
 */
const STATUS_TAG: Record<RunStatus, string> = {
  running: 'tag tag-accent',
  paused: 'tag tag-outline',
  stopped: 'tag tag-neutral',
}

export function RunHistory({
  runs,
  error,
  scopeRunId,
  onPick,
}: {
  runs: RunsResponse | null
  error: Error | null
  scopeRunId: number | undefined
  onPick: (runId: number) => void
}) {
  const items = runs?.page.items ?? []
  const right = { ...CELL, textAlign: 'right' as const }

  return (
    <>
      <div style={{ marginTop: 26, marginBottom: 20 }}>
        <h3 style={{ margin: '0 0 6px' }}>Runs</h3>
        <div style={{ fontSize: 13, color: MUTED(55) }}>
          {runs === null
            ? 'Reading run history…'
            : `${count(runs.page.totalItems)} runs recorded · newest first · pick one to scope the feed, networks and comparison to it.`}
        </div>
      </div>

      <InlineError error={error} prefix="Run history could not refresh" />

      <table className="table">
        <thead>
          <tr>
            <th>Run</th>
            <th>Status</th>
            <th>Started (UTC)</th>
            <th>Duration</th>
            <th>Models</th>
            <th style={{ textAlign: 'right' }}>Window</th>
            <th style={{ textAlign: 'right' }}>Clicks</th>
            <th style={{ textAlign: 'right' }}>Settled</th>
            <th style={{ textAlign: 'right' }}>Commission</th>
            <th style={{ textAlign: 'right' }}>Per settled</th>
            <th>Provenance</th>
            <th style={{ textAlign: 'right' }}>Scope</th>
          </tr>
        </thead>
        <tbody>
          {items.map((run) => {
            const viewing = run.id === scopeRunId
            const ended = run.stoppedAt ?? runs?.serverNow ?? run.startedAt
            return (
              <tr key={run.id} data-run-id={run.id} style={viewing ? { background: MUTED(4) } : undefined}>
                <td className="tnum" style={{ ...CELL, fontWeight: 500 }}>
                  #{run.id}
                </td>
                <td style={CELL}>
                  <span className={STATUS_TAG[run.status]}>{run.status}</span>
                </td>
                <td className="tnum" style={CELL}>
                  {utcDateTime(run.startedAt)}
                </td>
                <td className="tnum" style={CELL}>
                  {duration(elapsedMs(run.startedAt, ended))}
                  {run.stoppedAt === null ? <span style={{ color: MUTED(45) }}> so far</span> : null}
                </td>
                <td style={CELL}>
                  <span style={{ display: 'inline-flex', gap: 4, flexWrap: 'wrap', alignItems: 'center' }}>
                    {run.modelNames.map((name) => (
                      <span key={name} className="tag tag-accent">
                        {name}
                      </span>
                    ))}
                    {run.modelNames.length > 1 ? (
                      <span style={{ fontSize: 11, color: MUTED(45) }}>{run.interleaved ? 'interleaved' : 'sequential'}</span>
                    ) : null}
                  </span>
                </td>
                <td className="tnum" style={right}>
                  {run.evidenceWindowMinutes} min
                </td>
                <td className="tnum" style={right}>
                  {count(run.clicks)}
                </td>
                <td className="tnum" style={right}>
                  {count(run.settled)}
                </td>
                <td className="tnum" style={right}>
                  {money(run.totalCommission)}
                </td>
                <td className="tnum" style={right}>
                  {run.perSettledClick === null ? <span style={{ color: MUTED(35) }}>—</span> : rate(run.perSettledClick)}
                </td>
                <td style={{ ...CELL, fontSize: 12, color: MUTED(55) }}>
                  app {run.appVersion} · credential {run.credentialFingerprint}
                </td>
                <td style={right}>
                  {viewing ? (
                    <span className="tag tag-outline">viewing</span>
                  ) : (
                    <button type="button" className="btn btn-secondary" onClick={() => onPick(run.id)}>
                      View
                    </button>
                  )}
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>

      {runs !== null && items.length === 0 ? (
        <div style={{ padding: '18px 0', fontSize: 14, color: MUTED(45) }}>No runs yet. Start one above.</div>
      ) : null}
      {runs !== null && runs.page.totalItems > items.length ? (
        <div style={{ fontSize: 12, color: MUTED(45), marginTop: 14 }}>
          Showing the {count(items.length)} most recent of {count(runs.page.totalItems)} runs.
        </div>
      ) : null}
    </>
  )
}
