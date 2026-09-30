import { useState, type FormEvent } from 'react'
import { OperatorApiError, operatorApi } from '../api/operator'
import type { ActiveRun, CurrentRunResponse, ModelInfo, StartRunRequest } from '../api/types'
import { ago, duration, elapsedMs, utcTime } from '../lib/format'
import { InlineError } from './Notice'
import { KICKER, MUTED } from './style'

type Action = 'pause' | 'resume' | 'stop' | 'start'

/**
 * The active run and what the operator can do to it.
 *
 * Pause and Resume toggle on one button exactly as the original client's did. "Pause" is truthful
 * about what it does: it stops ISSUING clicks, while the host keeps polling so clicks already in
 * flight still reach a terminal state -- hence "paused · polling continues". Stop is final and asks
 * once before doing it.
 *
 * Starting a run is where the Affiliate Service is first contacted (catalogues are fetched and
 * snapshotted). A 502/503 there is the old "The client cannot start", and the parent shows it as
 * such; validation and state conflicts stay inline next to the form.
 */
export function RunControls({
  current,
  models,
  modelsError,
  onChanged,
  onUpstreamFailure,
}: {
  /** Null until the first read of `/api/runs/getcurrent` lands. */
  current: CurrentRunResponse | null
  models: ModelInfo[]
  modelsError: Error | null
  /** Called after any successful action so the parent re-reads the run straight away. */
  onChanged: () => void
  /** A 502/503 from the Affiliate Service, for the fatal-style banner. */
  onUpstreamFailure: (message: string) => void
}) {
  const [busy, setBusy] = useState<Action | null>(null)
  const [actionError, setActionError] = useState<Error | null>(null)
  const [confirmingStop, setConfirmingStop] = useState(false)

  const activeRun: ActiveRun | null = current?.run ?? null
  const serverNow = current?.serverNow ?? null
  const running = activeRun?.status === 'running'
  const paused = activeRun?.status === 'paused'

  const statusText =
    current === null
      ? 'connecting…'
      : running
        ? 'issuing · polling'
        : paused
          ? 'paused · polling continues'
          : 'no active run'

  async function perform(action: Action, call: () => Promise<unknown>): Promise<boolean> {
    setBusy(action)
    setActionError(null)
    try {
      await call()
      onChanged()
      return true
    } catch (cause) {
      const error = cause instanceof Error ? cause : new Error(String(cause))
      setActionError(error)
      if (error instanceof OperatorApiError && error.isUpstream) onUpstreamFailure(error.message)
      return false
    } finally {
      setBusy(null)
    }
  }

  return (
    <section
      className="card blueprint"
      aria-label="Run controls"
      style={{ padding: '16px 18px', gap: 12, marginBottom: 20 }}
    >
      <i className="corner tl" />
      <i className="corner tr" />
      <i className="corner bl" />
      <i className="corner br" />

      <div style={{ display: 'flex', gap: 24, alignItems: 'flex-start', flexWrap: 'wrap' }}>
        <div style={{ flex: 1, minWidth: 320, display: 'grid', gap: 6 }}>
          <div style={KICKER}>Active run</div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
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
            <span style={{ fontFamily: 'var(--font-heading)', fontWeight: 600, fontSize: 20, lineHeight: 1.1 }}>
              {activeRun === null ? 'No active run' : `Run #${activeRun.runId}`}
            </span>
            <span data-run-status={activeRun?.status ?? (current === null ? 'loading' : 'none')} style={{ fontSize: 12, color: MUTED(55) }}>
              {statusText}
            </span>
          </div>

          {activeRun !== null && serverNow !== null ? (
            <RunFacts run={activeRun} serverNow={serverNow} />
          ) : (
            <div style={{ fontSize: 13, color: MUTED(55) }}>
              {current === null
                ? 'Reading the host…'
                : 'No clicks are being issued. Commission polling continues regardless, so earlier runs still settle.'}
            </div>
          )}
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
          <button
            type="button"
            className="btn btn-secondary"
            disabled={activeRun === null || busy !== null}
            onClick={() => {
              if (activeRun === null) return
              void perform(running ? 'pause' : 'resume', () =>
                running ? operatorApi.pauseRun(activeRun.runId) : operatorApi.resumeRun(activeRun.runId),
              )
            }}
          >
            {running ? 'Pause loop' : 'Resume loop'}
          </button>

          {confirmingStop && activeRun !== null ? (
            <>
              <span style={{ fontSize: 12, color: MUTED(70) }}>Stop run #{activeRun.runId}? Stopping is final.</span>
              <button
                type="button"
                className="btn btn-primary"
                disabled={busy !== null}
                onClick={() => {
                  void perform('stop', () => operatorApi.stopRun(activeRun.runId)).then(() => setConfirmingStop(false))
                }}
              >
                Confirm stop
              </button>
              <button type="button" className="btn btn-ghost" onClick={() => setConfirmingStop(false)}>
                Keep running
              </button>
            </>
          ) : (
            <button
              type="button"
              className="btn btn-ghost"
              disabled={activeRun === null || busy !== null}
              onClick={() => setConfirmingStop(true)}
            >
              Stop run
            </button>
          )}
        </div>
      </div>

      {activeRun !== null ? <InlineError error={actionError} prefix="The action failed" /> : null}

      {current !== null && activeRun === null ? (
        <StartRunForm
          models={models}
          modelsError={modelsError}
          busy={busy === 'start'}
          error={actionError}
          onStart={(request) => perform('start', () => operatorApi.startRun(request))}
        />
      ) : null}
    </section>
  )
}

/** What the run is: which models decide, how, since when, and under which credentials and build. */
function RunFacts({ run, serverNow }: { run: ActiveRun; serverNow: string }) {
  return (
    <dl
      style={{
        display: 'grid',
        gridTemplateColumns: 'max-content 1fr',
        columnGap: 14,
        rowGap: 3,
        margin: 0,
        fontSize: 13,
      }}
    >
      <dt style={{ color: MUTED(55) }}>Models</dt>
      <dd style={{ margin: 0, display: 'flex', gap: 6, flexWrap: 'wrap', alignItems: 'center' }}>
        {run.modelNames.map((name) => (
          <span key={name} className="tag tag-accent" data-model-name={name}>
            {name}
          </span>
        ))}
        {run.modelNames.length > 1 ? (
          <span className={run.interleaved ? 'tag tag-outline' : 'tag tag-neutral'}>
            {run.interleaved ? 'interleaved · round-robin on the same traffic' : 'not interleaved · first model decides'}
          </span>
        ) : null}
      </dd>

      <dt style={{ color: MUTED(55) }}>Started</dt>
      <dd style={{ margin: 0 }} className="tnum">
        {utcTime(run.startedAt)} UTC · running for {duration(elapsedMs(run.startedAt, serverNow))}
        {run.pausedAt !== null ? ` · paused ${ago(run.pausedAt, serverNow)}` : ''}
      </dd>

      <dt style={{ color: MUTED(55) }}>Evidence window</dt>
      <dd style={{ margin: 0 }}>{run.evidenceWindowMinutes} min</dd>

      <dt style={{ color: MUTED(55) }}>Provenance</dt>
      <dd style={{ margin: 0, color: MUTED(70) }}>
        app {run.appVersion} · credential {run.credentialFingerprint}
      </dd>
    </dl>
  )
}

/**
 * The start form: which models, whether they share traffic, and how long "recent" is.
 *
 * Selection keeps the catalogue's order rather than click order, so an interleaved run's round-robin
 * is the same whichever box was ticked first.
 */
function StartRunForm({
  models,
  modelsError,
  busy,
  error,
  onStart,
}: {
  models: ModelInfo[]
  modelsError: Error | null
  busy: boolean
  error: Error | null
  onStart: (request: StartRunRequest) => Promise<boolean>
}) {
  const [chosen, setChosen] = useState<ReadonlySet<string>>(() => new Set())
  const [interleaved, setInterleaved] = useState(true)
  const [windowMinutes, setWindowMinutes] = useState('')

  const selected = models.filter((model) => chosen.has(model.name)).map((model) => model.name)
  const canInterleave = selected.length >= 2

  const toggle = (name: string) =>
    setChosen((previous) => {
      const next = new Set(previous)
      if (next.has(name)) next.delete(name)
      else next.add(name)
      return next
    })

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const trimmed = windowMinutes.trim()
    void onStart({
      modelNames: selected,
      interleaved: canInterleave && interleaved,
      evidenceWindowMinutes: trimmed === '' ? null : Number(trimmed),
    })
  }

  const details = error instanceof OperatorApiError ? (error.details ?? []) : []

  return (
    <form onSubmit={submit} style={{ display: 'grid', gap: 12, paddingTop: 8, borderTop: '1px solid var(--color-divider)' }}>
      <div>
        <div style={KICKER}>Start run</div>
        <div style={{ fontSize: 13, color: MUTED(55) }}>
          The host fetches the merchant and network catalogues, snapshots them as the run&apos;s provenance,
          and begins issuing one click every three seconds.
        </div>
      </div>

      <fieldset style={{ border: 0, margin: 0, padding: 0, display: 'grid', gap: 6 }}>
        <legend style={{ fontSize: 12, color: MUTED(70), marginBottom: 5, padding: 0 }}>Optimization models</legend>
        {modelsError !== null ? <InlineError error={modelsError} prefix="The model catalogue could not be read" /> : null}
        {models.length === 0 && modelsError === null ? (
          <div style={{ fontSize: 13, color: MUTED(45) }}>Reading the model catalogue…</div>
        ) : null}
        {models.map((model) => (
          <label
            key={model.name}
            style={{
              display: 'grid',
              gridTemplateColumns: 'auto max-content auto 1fr',
              alignItems: 'baseline',
              columnGap: 10,
              fontSize: 13,
              cursor: 'pointer',
            }}
          >
            <input
              type="checkbox"
              checked={chosen.has(model.name)}
              onChange={() => toggle(model.name)}
              style={{ accentColor: 'var(--color-accent)' }}
            />
            <span style={{ fontFamily: 'var(--font-heading)', fontWeight: 600, fontSize: 15 }}>{model.name}</span>
            <span style={{ fontSize: 11, color: MUTED(45) }} className="tnum">
              v{model.version}
            </span>
            <span style={{ color: MUTED(70) }}>{model.description}</span>
          </label>
        ))}
      </fieldset>

      <div style={{ display: 'flex', gap: 18, alignItems: 'flex-end', flexWrap: 'wrap' }}>
        <label
          style={{ display: 'inline-flex', alignItems: 'center', gap: 8, fontSize: 13, opacity: canInterleave ? 1 : 0.5 }}
        >
          <input
            type="checkbox"
            checked={canInterleave && interleaved}
            disabled={!canInterleave}
            onChange={(event) => setInterleaved(event.target.checked)}
            style={{ accentColor: 'var(--color-accent)' }}
          />
          Interleave models (round-robin on the same traffic)
        </label>

        <div className="field" style={{ width: 220 }}>
          <label htmlFor="start-window">Evidence window (minutes)</label>
          <input
            id="start-window"
            className="input"
            type="number"
            inputMode="numeric"
            min={1}
            max={120}
            placeholder="10 (server default)"
            value={windowMinutes}
            onChange={(event) => setWindowMinutes(event.target.value)}
          />
        </div>

        <button type="submit" className="btn btn-primary" disabled={busy}>
          {busy ? 'Starting…' : 'Start run'}
        </button>
      </div>

      {error !== null ? (
        <div role="alert" style={{ fontSize: 13, color: 'var(--color-accent-700)' }}>
          <div>{error.message}</div>
          {details.length > 0 ? (
            <ul style={{ margin: '4px 0 0', paddingLeft: 18 }}>
              {details.map((detail, index) => (
                <li key={`${detail.fieldName}-${index}`}>
                  <span style={{ fontWeight: 500 }}>{detail.fieldName}</span>: {detail.message}
                </li>
              ))}
            </ul>
          ) : null}
        </div>
      ) : null}
    </form>
  )
}
