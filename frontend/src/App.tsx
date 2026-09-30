import { useCallback, useEffect, useState } from 'react'
import { operatorApi } from './api/operator'
import type { ModelInfo } from './api/types'
import { POLL_INTERVAL_MS, usePolling } from './hooks/usePolling'
import { ClickFeed } from './views/ClickFeed'
import { ComparePanel } from './views/ComparePanel'
import { Counters } from './views/Counters'
import { HealthStrip } from './views/HealthStrip'
import { LoopEventsStrip } from './views/LoopEventsStrip'
import { NetworksPanel } from './views/NetworksPanel'
import { FatalBanner, InlineError } from './views/Notice'
import { RunControls } from './views/RunControls'
import { RunHistory } from './views/RunHistory'
import { MUTED } from './views/style'

type Tab = 'feed' | 'networks' | 'compare' | 'runs'

const TABS: { id: Tab; label: string }[] = [
  { id: 'feed', label: 'Feed' },
  { id: 'networks', label: 'Networks' },
  { id: 'compare', label: 'Compare' },
  { id: 'runs', label: 'Runs' },
]

/** How many runs the history reads. Plenty for a prototype; paging the history is a later change. */
const RUN_HISTORY_SIZE = 25

/**
 * The operator UI.
 *
 * Nothing runs here any more. The routing loop, the commission poll, the click store and every
 * derived number live in the .NET host; this component reads them every two seconds and draws them.
 * The one piece of state that is genuinely the operator's is the RUN SCOPE: which run the feed, the
 * network insight and the comparison describe. It defaults to the active run, falls back to the
 * latest one when nothing is active, and can be pointed at any run from the history.
 */
export function App() {
  const current = usePolling(operatorApi.getCurrentRun, POLL_INTERVAL_MS)
  const health = usePolling(operatorApi.getHealth, POLL_INTERVAL_MS)
  const runsFetcher = useCallback(() => operatorApi.getRuns({ pageIndex: 0, pageSize: RUN_HISTORY_SIZE }), [])
  const runs = usePolling(runsFetcher, POLL_INTERVAL_MS)

  // The model catalogue is code, not data: read once.
  const [models, setModels] = useState<ModelInfo[]>([])
  const [modelsError, setModelsError] = useState<Error | null>(null)
  useEffect(() => {
    let cancelled = false
    operatorApi.getModels().then(
      (list) => {
        if (!cancelled) setModels(list)
      },
      (cause: unknown) => {
        if (!cancelled) setModelsError(cause instanceof Error ? cause : new Error(String(cause)))
      },
    )
    return () => {
      cancelled = true
    }
  }, [])

  const [scope, setScope] = useState<number | undefined>(undefined)
  const [tab, setTab] = useState<Tab>('feed')
  const [fatal, setFatal] = useState<string | null>(null)

  const activeRun = current.data?.run ?? null
  const latestRunId = runs.data?.page.items[0]?.id
  const scopeRunId = scope ?? activeRun?.runId ?? latestRunId
  const scopedRun = runs.data?.page.items.find((run) => run.id === scopeRunId) ?? null
  const scopeIsActive = activeRun !== null && scopeRunId === activeRun.runId

  const insightFetcher = useCallback(() => operatorApi.getNetworkInsight(scopeRunId), [scopeRunId])
  const insight = usePolling(insightFetcher, POLL_INTERVAL_MS)

  const refreshRun = useCallback(() => {
    current.refresh()
    health.refresh()
    runs.refresh()
  }, [current.refresh, health.refresh, runs.refresh])

  const pickRun = (runId: number) => {
    setScope(activeRun !== null && runId === activeRun.runId ? undefined : runId)
    setTab('feed')
  }

  return (
    <div
      style={{
        minHeight: '100vh',
        background: 'var(--color-bg)',
        color: 'var(--color-text)',
        fontFamily: 'var(--font-body)',
        fontSize: 15,
      }}
    >
      <header style={{ maxWidth: 1240, margin: '0 auto', padding: '18px 40px 0' }}>
        <div className="nav" style={{ padding: '0 0 18px', gap: 32 }}>
          <div className="nav-brand" style={{ display: 'flex', alignItems: 'center', gap: 10, marginRight: 'auto' }}>
            <span style={{ fontFamily: 'var(--font-heading)', fontSize: 18, fontWeight: 600 }}>Affiliate Optimizer</span>
            <span
              style={{
                fontFamily: 'var(--font-body)',
                fontSize: 11,
                letterSpacing: '.14em',
                textTransform: 'uppercase',
                color: 'var(--color-accent)',
              }}
            >
              Operator
            </span>
          </div>

          <button type="button" className="btn btn-ghost" onClick={() => void operatorApi.signOut()}>
            Sign out
          </button>
        </div>

        <RunControls
          current={current.data}
          models={models}
          modelsError={modelsError}
          onChanged={refreshRun}
          onUpstreamFailure={setFatal}
        />
        <InlineError error={current.error} prefix="The run could not be read" />
      </header>

      <main style={{ maxWidth: 1240, margin: '0 auto', padding: '0 40px 96px' }}>
        {fatal !== null ? (
          <FatalBanner heading="The client cannot start" message={fatal} onDismiss={() => setFatal(null)} />
        ) : null}

        <HealthStrip health={health.data} error={health.error} />

        <Counters runId={scopeRunId} endedAt={scopedRun?.stoppedAt ?? null} />

        {health.data !== null ? (
          <LoopEventsStrip events={health.data.openEvents} serverNow={health.data.serverNow} onChanged={health.refresh} />
        ) : null}

        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 12,
            flexWrap: 'wrap',
            fontSize: 12,
            color: MUTED(55),
            marginTop: 6,
          }}
          data-scope={scopeRunId ?? ''}
        >
          <span>Scope:</span>
          {scopeRunId === undefined ? (
            <span>no runs yet</span>
          ) : (
            <span>
              <span style={{ fontWeight: 500, color: 'var(--color-text)' }}>run #{scopeRunId}</span>
              {scopeIsActive
                ? ' · active'
                : scopedRun !== null
                  ? ` · ${scopedRun.status} · historical`
                  : ' · historical'}
            </span>
          )}
          {activeRun !== null && !scopeIsActive ? (
            <button type="button" className="btn btn-ghost" onClick={() => setScope(undefined)}>
              Back to active run
            </button>
          ) : null}
        </div>

        <nav
          className="nav"
          role="tablist"
          aria-label="Panels"
          style={{ padding: 0, gap: 4, marginTop: 10, borderBottom: '1px solid var(--color-divider)' }}
        >
          {TABS.map((entry) => {
            const selected = entry.id === tab
            return (
              <button
                key={entry.id}
                type="button"
                role="tab"
                aria-selected={selected}
                aria-current={selected ? 'page' : undefined}
                data-tab={entry.id}
                onClick={() => setTab(entry.id)}
                style={{
                  background: 'transparent',
                  border: 0,
                  borderBottom: `2px solid ${selected ? 'var(--color-accent)' : 'transparent'}`,
                  marginBottom: -1,
                  padding: '10px 14px',
                  cursor: 'pointer',
                  fontFamily: 'var(--font-heading)',
                  fontWeight: 600,
                  fontSize: 15,
                  color: selected ? 'var(--color-accent)' : MUTED(70),
                }}
              >
                {entry.label}
              </button>
            )
          })}
        </nav>

        <div role="tabpanel">
          {tab === 'feed' ? (
            <ClickFeed runId={scopeRunId} networks={insight.data?.networks ?? []} models={models} />
          ) : tab === 'networks' ? (
            <NetworksPanel insight={insight.data} error={insight.error} />
          ) : tab === 'compare' ? (
            <ComparePanel runId={scopeRunId} />
          ) : (
            <RunHistory runs={runs.data} error={runs.error} scopeRunId={scopeRunId} onPick={pickRun} />
          )}
        </div>
      </main>
    </div>
  )
}
