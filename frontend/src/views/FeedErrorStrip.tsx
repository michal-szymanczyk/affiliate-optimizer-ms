import { AlertIcon } from './AlertIcon'
import type { FeedError } from '../loop/runLoop'

/**
 * A per-poll failure, shown above the table.
 *
 * Deliberately a different surface from a per-click `errorCode`. The two are different classes of
 * problem: a `MERCHANT_NOT_SUPPORTED` is one click the optimization model got wrong, while a
 * `RATE_LIMITED` or an `UNAUTHORIZED` is the whole loop being held up. Putting the second in a row
 * of the table would bury it.
 *
 * `UNAUTHORIZED` is not dismissible: nothing in the client works until the credentials are fixed, so
 * letting an operator hide it would only make the resulting silence harder to explain.
 */
export function FeedErrorStrip({ error, onDismiss }: { error: FeedError; onDismiss: () => void }) {
  return (
    <div
      className="blueprint"
      role="alert"
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 14,
        padding: '12px 16px',
        marginBottom: 26,
        background: 'color-mix(in srgb, var(--color-accent) 8%, transparent)',
      }}
    >
      <i className="corner tl" />
      <i className="corner tr" />
      <i className="corner bl" />
      <i className="corner br" />

      <AlertIcon size={16} color="var(--color-accent-700)" />
      <span style={{ fontSize: 13, fontWeight: 500, letterSpacing: '.02em' }}>{error.code}</span>
      <span style={{ fontSize: 13, color: 'color-mix(in srgb, var(--color-text) 70%, transparent)' }}>
        {error.message}
      </span>
      {error.dismissible ? (
        <button type="button" className="btn btn-ghost" style={{ marginLeft: 'auto' }} onClick={onDismiss}>
          Dismiss
        </button>
      ) : null}
    </div>
  )
}
