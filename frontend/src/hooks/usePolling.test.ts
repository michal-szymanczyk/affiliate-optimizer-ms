/**
 * The polling hook, on fake timers.
 *
 * The behaviours pinned here are the ones an operator would notice if they broke: a read on mount,
 * a cadence measured from completion, a manual refresh, an error that does not wipe the last good
 * data, and a hidden tab that stops asking.
 */
import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { usePolling } from './usePolling'

beforeEach(() => {
  vi.useFakeTimers()
})

afterEach(() => {
  vi.useRealTimers()
  vi.restoreAllMocks()
})

describe('usePolling', () => {
  it('reads once on mount and again after the interval', async () => {
    let calls = 0
    const fetcher = vi.fn(async () => ++calls)
    const { result } = renderHook(() => usePolling(fetcher, 2_000))

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(result.current.data).toBe(1)
    expect(result.current.error).toBeNull()

    await act(async () => {
      await vi.advanceTimersByTimeAsync(1_900)
    })
    expect(fetcher).toHaveBeenCalledTimes(1)

    await act(async () => {
      await vi.advanceTimersByTimeAsync(200)
    })
    expect(fetcher).toHaveBeenCalledTimes(2)
    expect(result.current.data).toBe(2)
  })

  it('reads immediately on refresh()', async () => {
    const fetcher = vi.fn(async () => 'ok')
    const { result } = renderHook(() => usePolling(fetcher, 60_000))

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(fetcher).toHaveBeenCalledTimes(1)

    await act(async () => {
      result.current.refresh()
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(fetcher).toHaveBeenCalledTimes(2)
  })

  it('keeps the last good data when a read fails, and clears the error when the next one succeeds', async () => {
    let attempt = 0
    const fetcher = vi.fn(async () => {
      attempt += 1
      if (attempt === 2) throw new Error('host restarting')
      return attempt
    })
    const { result } = renderHook(() => usePolling(fetcher, 1_000))

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(result.current.data).toBe(1)

    await act(async () => {
      await vi.advanceTimersByTimeAsync(1_000)
    })
    expect(result.current.data).toBe(1)
    expect(result.current.error?.message).toBe('host restarting')

    await act(async () => {
      await vi.advanceTimersByTimeAsync(1_000)
    })
    expect(result.current.data).toBe(3)
    expect(result.current.error).toBeNull()
  })

  it('restarts at once when the fetcher identity changes', async () => {
    const first = vi.fn(async () => 'first')
    const second = vi.fn(async () => 'second')
    const { result, rerender } = renderHook(({ fetcher }) => usePolling(fetcher, 60_000), {
      initialProps: { fetcher: first },
    })

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(result.current.data).toBe('first')

    rerender({ fetcher: second })
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(second).toHaveBeenCalledTimes(1)
    expect(result.current.data).toBe('second')
  })

  it('stops polling while the tab is hidden and reads again when it is shown', async () => {
    const fetcher = vi.fn(async () => 'ok')
    const hidden = vi.spyOn(document, 'hidden', 'get')
    hidden.mockReturnValue(false)
    renderHook(() => usePolling(fetcher, 1_000))

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(fetcher).toHaveBeenCalledTimes(1)

    hidden.mockReturnValue(true)
    await act(async () => {
      document.dispatchEvent(new Event('visibilitychange'))
      await vi.advanceTimersByTimeAsync(5_000)
    })
    expect(fetcher).toHaveBeenCalledTimes(1)

    hidden.mockReturnValue(false)
    await act(async () => {
      document.dispatchEvent(new Event('visibilitychange'))
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(fetcher).toHaveBeenCalledTimes(2)
  })

  it('stops on unmount', async () => {
    const fetcher = vi.fn(async () => 'ok')
    const { unmount } = renderHook(() => usePolling(fetcher, 1_000))

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    unmount()
    await act(async () => {
      await vi.advanceTimersByTimeAsync(10_000)
    })
    expect(fetcher).toHaveBeenCalledTimes(1)
  })
})
