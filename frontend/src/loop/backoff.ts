/**
 * When to make the next call.
 *
 * Pure scheduling arithmetic, kept out of the loop so it can be tested without a clock or a server.
 *
 * The client is rationed: 20 clicks/min, bursting to 40, and 60 commission polls/min. Issuing
 * faster than the ration only earns 429s, so the loop paces itself to the sustained rate rather
 * than sprinting into the throttle and backing off — a client that has to be told to slow down is
 * one that has already wasted a request.
 */

/** Server-published limits (openapi.json / README). Kept here as the client's own copy. */
export const CLICK_RATE_PER_MINUTE = 20
export const COMMISSION_POLL_RATE_PER_MINUTE = 60

/** Sustained pacing: one click every 3s, one poll every 2s. */
export const CLICK_INTERVAL_MS = 60_000 / CLICK_RATE_PER_MINUTE
export const POLL_INTERVAL_MS = 2_000

/** Ceiling on the exponential backoff, so the loop always recovers on its own. */
export const MAX_BACKOFF_MS = 30_000

/**
 * How long to wait after a failure.
 *
 * `retryAfterSeconds` is honored exactly when the server sends it: it is computed from the real
 * bucket state, so a client that waits that long is admitted, and guessing longer just wastes
 * ration. Anything else gets a bounded exponential backoff — enough to stop hammering a server that
 * is unwell, capped so the loop recovers once it is not.
 */
export function backoffAfterFailure(
  attempt: number,
  retryAfterSeconds: number | undefined,
  baseMs = 1_000,
): number {
  if (retryAfterSeconds !== undefined) {
    // Never below one second: a 0 would hot-loop.
    return Math.max(1_000, Math.ceil(retryAfterSeconds * 1_000))
  }
  if (attempt < 1) {
    throw new Error(`backoffAfterFailure: attempt must be at least 1, got ${attempt}`)
  }
  return Math.min(MAX_BACKOFF_MS, baseMs * 2 ** (attempt - 1))
}
