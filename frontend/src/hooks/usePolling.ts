import { useCallback, useEffect, useState } from 'react'

/** Every panel re-reads the host on this cadence. The host's own loops run regardless. */
export const POLL_INTERVAL_MS = 2_000

/**
 * Polls a fetcher on a fixed cadence and exposes the latest result.
 *
 * This replaced the in-browser loop and its one-second `setNow` tick. The UI no longer computes
 * anything from its own clock: every response carries `serverNow`, and the view derives ages and
 * durations from that. So there is nothing to tick; the panel simply re-reads the truth every
 * `intervalMs` and re-renders.
 *
 * Semantics worth knowing:
 *   - The fetcher runs immediately, then `intervalMs` after each COMPLETION, so slow responses never
 *     pile up. A `refresh()` runs it straight away (after an action, say).
 *   - A hidden tab stops polling and resumes with an immediate read when it becomes visible again --
 *     the host keeps running either way, so a hidden tab misses nothing.
 *   - Pass a STABLE fetcher (`useCallback`). A new identity restarts polling at once, which is what a
 *     changed filter or run scope wants. The previous data stays on screen until the new read lands,
 *     so a filter change never flashes an empty table.
 *   - A failed read keeps the last good `data` and sets `error`; the next success clears it.
 */
export interface Polling<T> {
  data: T | null
  error: Error | null
  /** Re-read now, outside the cadence. */
  refresh: () => void
}

export function usePolling<T>(fetcher: () => Promise<T>, intervalMs: number): Polling<T> {
  const [data, setData] = useState<T | null>(null)
  const [error, setError] = useState<Error | null>(null)
  const [generation, setGeneration] = useState(0)

  useEffect(() => {
    let cancelled = false
    let inFlight = false
    let timer: ReturnType<typeof setTimeout> | null = null

    const clearTimer = () => {
      if (timer !== null) clearTimeout(timer)
      timer = null
    }

    const schedule = () => {
      clearTimer()
      if (cancelled || document.hidden) return
      timer = setTimeout(() => void run(), intervalMs)
    }

    const run = async () => {
      if (cancelled || inFlight) return
      inFlight = true
      try {
        const result = await fetcher()
        if (!cancelled) {
          setData(result)
          setError(null)
        }
      } catch (cause) {
        if (!cancelled) setError(cause instanceof Error ? cause : new Error(String(cause)))
      } finally {
        inFlight = false
        schedule()
      }
    }

    const onVisibilityChange = () => {
      if (document.hidden) clearTimer()
      else if (!inFlight) void run()
    }

    document.addEventListener('visibilitychange', onVisibilityChange)
    void run()

    return () => {
      cancelled = true
      clearTimer()
      document.removeEventListener('visibilitychange', onVisibilityChange)
    }
  }, [fetcher, intervalMs, generation])

  const refresh = useCallback(() => setGeneration((n) => n + 1), [])

  return { data, error, refresh }
}
