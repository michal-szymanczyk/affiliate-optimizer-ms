import type { Diagnosis, HealthStatus, LoopBeat, RecentCounts } from '../api/types'
import { ago, count } from '../lib/format'
import { AlertIcon } from './AlertIcon'
import { InlineError } from './Notice'
import { MUTED, WARN_TAG } from './style'

/**
 * Operational observability in one strip: is the system healthy, and if not, WHERE is the fault.
 *
 * The host's diagnosis already localises a problem to credentials, the application, traffic (the
 * server), the data feed or an optimization model. This strip shows that verdict first, then the raw
 * ages and counters it was derived from, so an operator can check the reasoning rather than trust a
 * badge.
 */
const DIAGNOSIS: Record<Diagnosis, { label: string; fault: string | null; severity: 'ok' | 'neutral' | 'warning' }> = {
  healthy: { label: 'Healthy', fault: null, severity: 'ok' },
  noActiveRun: { label: 'No active run', fault: null, severity: 'neutral' },
  paused: { label: 'Paused', fault: null, severity: 'neutral' },
  unauthorized: { label: 'Unauthorized', fault: 'credentials', severity: 'warning' },
  appNotTicking: { label: 'App not ticking', fault: 'the application', severity: 'warning' },
  trafficStalled: { label: 'Traffic stalled', fault: 'traffic (the server)', severity: 'warning' },
  feedStalled: { label: 'Feed stalled', fault: 'the data feed', severity: 'warning' },
  modelFaulting: { label: 'Model faulting', fault: 'an optimization model', severity: 'warning' },
}

const RECENT_LABELS: { key: keyof RecentCounts; label: string }[] = [
  { key: 'rateLimited', label: 'rate-limited' },
  { key: 'transport', label: 'transport' },
  { key: 'unconfirmed', label: 'unconfirmed routes' },
  { key: 'modelErrors', label: 'model errors' },
  { key: 'rejections', label: 'rejections' },
  { key: 'clientErrors', label: 'client errors' },
]

function DiagnosisBadge({ diagnosis }: { diagnosis: Diagnosis }) {
  const meta = DIAGNOSIS[diagnosis]
  const className = meta.severity === 'ok' ? 'tag tag-accent' : meta.severity === 'neutral' ? 'tag tag-neutral' : 'tag'
  return (
    <span
      className={className}
      data-diagnosis={diagnosis}
      style={{ gap: 5, fontWeight: 500, ...(meta.severity === 'warning' ? WARN_TAG : {}) }}
    >
      {meta.severity === 'warning' ? <AlertIcon size={12} /> : null}
      {meta.label}
    </span>
  )
}

function Beat({ name, beat }: { name: string; beat: LoopBeat | null }) {
  // `ageSeconds` is computed on the host against its own clock; nothing here is derived locally.
  return (
    <span className="tnum">
      {name}: {beat === null ? 'no heartbeat yet' : `${beat.outcome} ${Math.round(beat.ageSeconds)}s ago`}
    </span>
  )
}

export function HealthStrip({ health, error }: { health: HealthStatus | null; error: Error | null }) {
  if (health === null) {
    return (
      <div style={{ marginBottom: 14, fontSize: 12, color: MUTED(45) }}>
        {error === null ? 'Reading health…' : <InlineError error={error} prefix="Health could not be read" />}
      </div>
    )
  }

  const meta = DIAGNOSIS[health.diagnosis]
  const now = health.serverNow
  const recent = RECENT_LABELS.filter(({ key }) => health.recent[key] > 0)

  return (
    <section
      className="blueprint"
      aria-label="Health"
      style={{ padding: '12px 16px', marginBottom: 14, display: 'grid', gap: 6, fontSize: 12 }}
    >
      <i className="corner tl" />
      <i className="corner tr" />
      <i className="corner bl" />
      <i className="corner br" />

      <div style={{ display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
        <DiagnosisBadge diagnosis={health.diagnosis} />
        <span style={{ fontSize: 13 }}>{health.summary}</span>
        {meta.fault !== null ? (
          <span style={{ fontSize: 12, color: 'var(--color-accent-700)' }}>fault is in {meta.fault}</span>
        ) : null}
      </div>

      <div style={{ display: 'flex', gap: 14, flexWrap: 'wrap', color: MUTED(60) }}>
        <span>last click {ago(health.lastClickIssuedAt, now)}</span>
        <span>last route {ago(health.lastRouteAt, now)}</span>
        <span>last disclosure {ago(health.lastDisclosureAt, now)}</span>
        <span>
          feed polled {ago(health.lastPollAt, now)}
          {health.lastPollSuccessAt !== health.lastPollAt ? ` (last success ${ago(health.lastPollSuccessAt, now)})` : ''}
        </span>
        <span>cursor {health.hasCursor ? 'held' : 'none'}</span>
        <span>{count(health.awaitingDisclosure)} awaiting disclosure</span>
        {health.overduePending > 0 ? (
          <span style={{ color: 'var(--color-accent-700)' }}>{count(health.overduePending)} overdue</span>
        ) : null}
        {health.clickConsecutiveFailures > 0 ? (
          <span style={{ color: 'var(--color-accent-700)' }}>
            click loop failing ×{health.clickConsecutiveFailures}
          </span>
        ) : null}
        {health.pollConsecutiveFailures > 0 ? (
          <span style={{ color: 'var(--color-accent-700)' }}>poll loop failing ×{health.pollConsecutiveFailures}</span>
        ) : null}
      </div>

      <div style={{ display: 'flex', gap: 14, flexWrap: 'wrap', color: MUTED(60) }}>
        <Beat name="click loop" beat={health.clickLoop} />
        <Beat name="poll loop" beat={health.pollLoop} />
        {recent.length > 0 ? (
          <span style={{ color: 'var(--color-accent-700)' }}>
            last 5 min: {recent.map(({ key, label }) => `${count(health.recent[key])} ${label}`).join(' · ')}
          </span>
        ) : (
          <span>no faults in the last 5 min</span>
        )}
      </div>

      <InlineError error={error} prefix="Health could not refresh" />
    </section>
  )
}
