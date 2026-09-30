import { useState } from 'react'
import { operatorApi } from '../api/operator'
import type { LoopEventKind, OpenEvent } from '../api/types'
import { ago, parseIso, shortClickId, until } from '../lib/format'
import { AlertIcon } from './AlertIcon'
import { MUTED } from './style'

/**
 * The host's open loop events, newest first, above the table.
 *
 * Deliberately a different surface from a per-click `errorCode`. The two are different classes of
 * problem: a `MERCHANT_NOT_SUPPORTED` is one click the optimization model got wrong, while a
 * `RATE_LIMITED` or an `UNAUTHORIZED` is the whole loop being held up. Putting the second in a row
 * of the table would bury it.
 *
 * `UNAUTHORIZED` is not dismissible: nothing in the loop works until the credentials are fixed, so
 * letting an operator hide it would only make the resulting silence harder to explain. The host
 * enforces that (412) and the button is simply not drawn.
 */
const KIND_LABELS: Record<LoopEventKind, string> = {
  api: 'server error',
  transport: 'transport',
  unconfirmed: 'unconfirmed route',
  clientError: 'client error',
  modelError: 'model error',
  info: 'info',
}

export function LoopEventsStrip({
  events,
  serverNow,
  onChanged,
}: {
  events: OpenEvent[]
  serverNow: string
  /** Called after a dismissal so the parent re-reads health straight away. */
  onChanged: () => void
}) {
  const [busyId, setBusyId] = useState<number | null>(null)
  const [failures, setFailures] = useState<Record<number, string>>({})

  const sorted = [...events].sort((a, b) => parseIso(b.occurredAt) - parseIso(a.occurredAt))
  if (sorted.length === 0) return null

  const dismiss = async (id: number) => {
    setBusyId(id)
    try {
      await operatorApi.dismissLoopEvent(id)
      onChanged()
    } catch (cause) {
      setFailures((previous) => ({ ...previous, [id]: cause instanceof Error ? cause.message : String(cause) }))
    } finally {
      setBusyId(null)
    }
  }

  return (
    <div style={{ display: 'grid', gap: 8, marginBottom: 26 }} data-loop-events="">
      {sorted.map((event) => {
        const info = event.kind === 'info'
        const failure = failures[event.id]
        return (
          <div
            key={event.id}
            className="blueprint"
            role={info ? 'status' : 'alert'}
            data-loop-event-code={event.code}
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: 14,
              padding: '12px 16px',
              background: info
                ? 'color-mix(in srgb, var(--color-text) 3%, transparent)'
                : 'color-mix(in srgb, var(--color-accent) 8%, transparent)',
            }}
          >
            <i className="corner tl" />
            <i className="corner tr" />
            <i className="corner bl" />
            <i className="corner br" />

            <AlertIcon size={16} color={info ? MUTED(45) : 'var(--color-accent-700)'} />
            <div style={{ display: 'grid', gap: 2, flex: 1, minWidth: 0 }}>
              <div style={{ display: 'flex', gap: 10, alignItems: 'baseline', flexWrap: 'wrap' }}>
                <span style={{ fontSize: 13, fontWeight: 500, letterSpacing: '.02em' }}>{event.code}</span>
                <span style={{ fontSize: 13, color: MUTED(70) }}>{event.message}</span>
              </div>
              <div style={{ fontSize: 11, color: MUTED(45), display: 'flex', gap: 10, flexWrap: 'wrap' }}>
                <span>{event.loop} loop</span>
                <span>{KIND_LABELS[event.kind]}</span>
                {event.clickId !== null ? <span className="tnum">click {shortClickId(event.clickId)}</span> : null}
                {event.runId !== null ? <span>run #{event.runId}</span> : null}
                <span>{ago(event.occurredAt, serverNow)}</span>
                {event.retryAt !== null ? <span>retry {until(event.retryAt, serverNow)}</span> : null}
                {failure !== undefined ? (
                  <span style={{ color: 'var(--color-accent-700)' }}>could not dismiss: {failure}</span>
                ) : null}
              </div>
            </div>
            {event.dismissible ? (
              <button
                type="button"
                className="btn btn-ghost"
                disabled={busyId === event.id}
                onClick={() => void dismiss(event.id)}
              >
                Dismiss
              </button>
            ) : null}
          </div>
        )
      })}
    </div>
  )
}
