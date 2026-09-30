import type { CSSProperties } from 'react'

/** Text at a fraction of the foreground colour, the way Industry mutes secondary copy. */
export const MUTED = (percent: number) => `color-mix(in srgb, var(--color-text) ${percent}%, transparent)`

/** The feed's cell padding: a little taller than Industry's default so tags fit. */
export const CELL: CSSProperties = { padding: '12px var(--space-2)' }

/** Small uppercase kicker, as on the counter tiles. */
export const KICKER: CSSProperties = {
  fontSize: 10,
  letterSpacing: '.1em',
  textTransform: 'uppercase',
  color: 'var(--color-accent)',
}

/** A warning tag: Industry has no danger colour, so the deep accent stands in for it. */
export const WARN_TAG: CSSProperties = {
  background: 'var(--color-accent-700)',
  color: 'var(--color-bg)',
}
