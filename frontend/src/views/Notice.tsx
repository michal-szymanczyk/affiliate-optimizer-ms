import { AlertIcon } from './AlertIcon'
import { MUTED } from './style'

/**
 * The full-width alert, as the original client drew it for "The client cannot start".
 *
 * Blueprint frame with registration marks, a heading, the message, and -- new -- a Dismiss button:
 * the host keeps running when the Affiliate Service refuses a start, so the operator can fix the
 * credentials and try again without reloading.
 */
export function FatalBanner({
  heading,
  message,
  onDismiss,
}: {
  heading: string
  message: string
  onDismiss?: () => void
}) {
  return (
    <div
      className="blueprint"
      role="alert"
      style={{
        display: 'flex',
        gap: 14,
        alignItems: 'flex-start',
        padding: '16px 18px',
        marginBottom: 20,
        background: 'color-mix(in srgb, var(--color-accent) 8%, transparent)',
      }}
    >
      <i className="corner tl" />
      <i className="corner tr" />
      <i className="corner bl" />
      <i className="corner br" />
      <AlertIcon size={16} color="var(--color-accent-700)" />
      <div style={{ flex: 1 }}>
        <div style={{ fontSize: 13, fontWeight: 500, marginBottom: 4 }}>{heading}</div>
        <div style={{ fontSize: 13, color: MUTED(70) }}>{message}</div>
      </div>
      {onDismiss ? (
        <button type="button" className="btn btn-ghost" onClick={onDismiss}>
          Dismiss
        </button>
      ) : null}
    </div>
  )
}

/** A one-line failure under a panel: the last good data stays on screen above it. */
export function InlineError({ error, prefix = 'Could not refresh' }: { error: Error | null; prefix?: string }) {
  if (error === null) return null
  return (
    <div
      role="alert"
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 6,
        fontSize: 12,
        margin: '8px 0',
        color: 'var(--color-accent-700)',
      }}
    >
      <AlertIcon size={12} />
      <span>
        {prefix}: {error.message}
      </span>
    </div>
  )
}
