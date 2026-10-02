import { useEffect, useMemo, useReducer, useRef, useState } from 'react'
import { AffiliateServiceClient } from './api/client'
import { readConfig } from './api/config'
import { ClickLog, decisionSummary, runCounters } from './loop/clickLog'
import { RunLoop } from './loop/runLoop'
import { randomOptimizationModel } from './models/random'
import { thompsonSamplingModel, type ThompsonModel } from './models/thompson'
import { NO_FILTERS, type Filters } from './lib/filter'
import { NO_SORT, type SortState } from './lib/sort'
import { BeliefsPanel } from './views/BeliefsPanel'
import { ClickFeed } from './views/ClickFeed'
import { Counters } from './views/Counters'
import { FeedErrorStrip } from './views/FeedErrorStrip'
import { ModelSelect } from './views/ModelSelect'

/** One tick a second drives the runtime counter and every derived click state. */
const TICK_MS = 1_000

/**
 * The whole client.
 *
 * The loop is NOT React state. It is a plain object held in a ref that calls `onChange` when
 * something happened, and the component re-renders from it. Putting the click log in state would
 * mean copying the whole array on every one of the thousands of updates a run produces, and it would
 * put a network loop's lifecycle at the mercy of React's — including StrictMode's double-mount,
 * which would run two loops and burn the click ration twice as fast.
 */
export function App() {
  const [, forceRender] = useReducer((n: number) => n + 1, 0)
  const [now, setNow] = useState(() => Date.now())
  const [fatal, setFatal] = useState<string | null>(null)
  const [filters, setFilters] = useState<Filters>(NO_FILTERS)
  const [sort, setSort] = useState<SortState>(NO_SORT)

  const log = useMemo(() => new ClickLog(), [])
  const loopRef = useRef<RunLoop | null>(null)
  const modelsRef = useRef<{ thompson: ThompsonModel; names: readonly string[] } | null>(null)

  useEffect(() => {
    let config
    try {
      config = readConfig()
    } catch (cause) {
      // Missing credentials: say so plainly rather than letting the loop emit a stream of 401s
      // that look like a server problem.
      setFatal(cause instanceof Error ? cause.message : String(cause))
      return
    }

    const thompson = thompsonSamplingModel()
    const models = [thompson, randomOptimizationModel()]

    const loop = new RunLoop({
      client: new AffiliateServiceClient(config),
      models,
      log,
      onChange: forceRender,
    })
    loopRef.current = loop
    modelsRef.current = { thompson, names: models.map((model) => model.name) }

    loop.start().catch((cause: unknown) => {
      setFatal(cause instanceof Error ? cause.message : String(cause))
    })

    return () => {
      loop.stop()
      loopRef.current = null
      modelsRef.current = null
    }
  }, [log])

  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), TICK_MS)
    return () => clearInterval(id)
  }, [])

  const loop = loopRef.current
  const models = modelsRef.current
  const clicks = log.all()
  const counters = runCounters(clicks, now)
  const running = loop?.state.running ?? false
  const feedError = loop?.state.feedError ?? null
  const modelError = loop?.state.modelError ?? null
  const activeModelName = loop?.state.activeModelName ?? ''

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
      <header className="nav" style={{ maxWidth: 1240, margin: '0 auto', padding: '18px 40px', gap: 32 }}>
        <div className="nav-brand" style={{ display: 'flex', alignItems: 'center', gap: 10, marginRight: 'auto' }}>
          <span style={{ fontFamily: 'var(--font-heading)', fontSize: 18, fontWeight: 600 }}>
            Affiliate Optimizer
          </span>
          <span
            style={{
              fontFamily: 'var(--font-body)',
              fontSize: 11,
              letterSpacing: '.14em',
              textTransform: 'uppercase',
              color: 'var(--color-accent)',
            }}
          >
            Client
          </span>
        </div>

        <ModelSelect
          names={models?.names ?? []}
          active={activeModelName}
          disabled={loop === null || fatal !== null}
          onSelect={(name) => loop?.selectModel(name)}
        />

        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 8,
            fontSize: 12,
            color: 'color-mix(in srgb, var(--color-text) 55%, transparent)',
          }}
        >
          <span
            aria-hidden="true"
            style={{
              width: 7,
              height: 7,
              background: 'var(--color-accent)',
              animation: running ? 'ind-pulse 1.8s ease-in-out infinite' : 'none',
              opacity: running ? 1 : 0.3,
            }}
          />
          {running ? 'polling' : 'paused'}
        </div>

        <button
          type="button"
          className="btn btn-secondary"
          disabled={loop === null || fatal !== null}
          onClick={() => (running ? loop?.pause() : loop?.resume())}
        >
          {running ? 'Pause loop' : 'Resume loop'}
        </button>
      </header>

      <main style={{ maxWidth: 1240, margin: '0 auto', padding: '20px 40px 96px' }}>
        {fatal !== null ? (
          <div
            className="blueprint"
            role="alert"
            style={{
              display: 'flex',
              gap: 14,
              padding: '16px 18px',
              background: 'color-mix(in srgb, var(--color-accent) 8%, transparent)',
            }}
          >
            <i className="corner tl" />
            <i className="corner tr" />
            <i className="corner bl" />
            <i className="corner br" />
            <div>
              <div style={{ fontSize: 13, fontWeight: 500, marginBottom: 4 }}>The client cannot start</div>
              <div style={{ fontSize: 13, color: 'color-mix(in srgb, var(--color-text) 70%, transparent)' }}>
                {fatal}
              </div>
            </div>
          </div>
        ) : (
          <>
            <Counters counters={counters} startedAt={loop?.state.startedAt ?? null} now={now} />

            {feedError !== null ? (
              <FeedErrorStrip error={feedError} onDismiss={() => loop?.dismissFeedError()} />
            ) : null}

            {modelError !== null ? (
              <FeedErrorStrip error={modelError} onDismiss={() => loop?.dismissModelError()} />
            ) : null}

            {models !== null ? (
              <BeliefsPanel
                networks={loop?.state.networks ?? []}
                beliefs={models.thompson.beliefs(now)}
                summary={decisionSummary(clicks, models.thompson.name)}
                deciding={activeModelName === models.thompson.name}
                now={now}
              />
            ) : null}

            <ClickFeed
              clicks={clicks}
              networks={loop?.state.networks ?? []}
              now={now}
              filters={filters}
              onFilters={setFilters}
              sort={sort}
              onSort={setSort}
            />
          </>
        )}
      </main>
    </div>
  )
}
