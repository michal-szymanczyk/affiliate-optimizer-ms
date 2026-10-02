/** Pure graph arithmetic. Every network shares the same money scale. */
export interface Distribution {
  mean: number
  sd: number
}

export interface DistributionBounds {
  xMin: number
  xMax: number
  /** The tallest density peak plus headroom, for the view that compares densities on one scale. */
  yMax: number
}

/** How one network's curve is told apart from the others. */
export interface SeriesStyle {
  /** A CSS color: one of the eight categorical slots defined on `.belief-distributions` in app.css. */
  color: string
  /** An SVG `stroke-dasharray`. Undefined draws a solid line. */
  dash: string | undefined
}

const SQRT_TWO_PI = Math.sqrt(2 * Math.PI)
const TAIL_SD = 4
const SERIES_SLOTS = 8
const SERIES_DASHES = [undefined, '7 4', '2 3'] as const

/** Below this resolution, a vertical marker is more honest than an invented Gaussian width. */
export function isPointDistribution({ mean, sd }: Distribution): boolean {
  return sd <= Math.max(1, Math.abs(mean)) * Number.EPSILON * 8
}

export function isValidDistribution({ mean, sd }: Distribution): boolean {
  return Number.isFinite(mean) && Number.isFinite(sd) && sd >= 0 &&
    Number.isFinite(mean - TAIL_SD * sd) && Number.isFinite(mean + TAIL_SD * sd)
}

export function normalDensity(x: number, { mean, sd }: Distribution): number {
  if (sd <= 0) return 0 // A point mass has no finite probability density.
  const z = (x - mean) / sd
  return Math.exp(-0.5 * z * z) / (sd * SQRT_TWO_PI)
}

export function distributionBounds(distributions: readonly Distribution[]): DistributionBounds | null {
  const valid = distributions.filter(isValidDistribution)
  if (valid.length === 0) return null
  let low = Infinity
  let high = -Infinity
  let peak = 0
  for (const distribution of valid) {
    low = Math.min(low, distribution.mean - TAIL_SD * distribution.sd)
    high = Math.max(high, distribution.mean + TAIL_SD * distribution.sd)
    if (!isPointDistribution(distribution)) peak = Math.max(peak, normalDensity(distribution.mean, distribution))
  }
  const span = Math.max(high - low, Math.max(1, Math.abs(low), Math.abs(high)) * 1e-6)
  const padding = span * 0.05
  const bounds = { xMin: low - padding, xMax: high + padding, yMax: peak > 0 ? peak * 1.1 : 1 }
  return Object.values(bounds).every(Number.isFinite) && bounds.xMax > bounds.xMin ? bounds : null
}

/**
 * Keeps the axes from twitching on every small change.
 *
 * A range that has lost at least a quarter of its width is taken as it is: it is exact, so nothing
 * falls outside it. Otherwise the old range is only ever widened, which ignores small contractions
 * and follows any expansion at once. Deciding the shrink first matters when the range moves
 * sideways: one edge grows while the whole narrows, and widening first would draw a frame that
 * spans both the old and the new range before the next one corrects it.
 */
export function stabilizeBounds(previous: DistributionBounds, next: DistributionBounds): DistributionBounds {
  const shrinkX = next.xMax - next.xMin < (previous.xMax - previous.xMin) * 0.75
  return {
    xMin: shrinkX ? next.xMin : Math.min(previous.xMin, next.xMin),
    xMax: shrinkX ? next.xMax : Math.max(previous.xMax, next.xMax),
    yMax: next.yMax > previous.yMax || next.yMax < previous.yMax * 0.75 ? next.yMax : previous.yMax,
  }
}

/** Sample in this curve's own standard deviations so a narrow peak is never skipped. */
export function distributionPoints(distribution: Distribution): readonly { x: number; y: number }[] {
  if (!isValidDistribution(distribution) || isPointDistribution(distribution)) return []
  return Array.from({ length: 161 }, (_, index) => {
    const z = -TAIL_SD + index * (2 * TAIL_SD / 160)
    const x = distribution.mean + z * distribution.sd
    return { x, y: normalDensity(x, distribution) }
  })
}

export function niceTicks(min: number, max: number, intervals = 5): { values: number[]; step: number } {
  const rawStep = (max - min) / Math.max(1, intervals)
  const power = 10 ** Math.floor(Math.log10(rawStep))
  const fraction = rawStep / power
  const step = (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10) * power
  if (!Number.isFinite(step) || step <= 0) return { values: [], step: 1 }
  const start = Math.ceil(min / step)
  const end = Math.floor(max / step)
  const values = Array.from({ length: Math.min(100, Math.max(0, end - start + 1)) }, (_, index) => {
    const value = (start + index) * step
    return Math.abs(value) < step * 1e-9 ? 0 : Number(value.toPrecision(12))
  })
  return { values, step }
}

/** Keep four CPC decimals, adding precision when the visible range demands it. */
export function formatCpcTick(value: number, step: number): string {
  const decimals = Math.max(4, -Math.floor(Math.log10(step)))
  const size = Math.abs(value)
  // The tails of a wide belief reach below zero. The sign goes before the currency, not inside it.
  return `${value < 0 ? '-' : ''}$${decimals > 8 ? size.toExponential(2) : size.toFixed(decimals)}`
}

/**
 * One style per network, following the network and never its rank: hiding, sorting or re-ranking
 * the curves repaints none of them. Only a change to the set of networks can.
 *
 * The ids are put in a fixed order and take the eight categorical slots in sequence. Eight is as
 * many hues as stay tellable apart, so a ninth network does not get an invented ninth hue: it takes
 * the first slot again with a dashed line, and a seventeenth takes it dotted. Past twenty-four the
 * patterns repeat, and the legend's names are what is left.
 */
export function seriesStyles(networkIds: readonly string[]): Map<string, SeriesStyle> {
  const styles = new Map<string, SeriesStyle>()
  Array.from(new Set(networkIds)).sort().forEach((networkId, index) => {
    styles.set(networkId, {
      color: `var(--series-${index % SERIES_SLOTS + 1})`,
      dash: SERIES_DASHES[Math.floor(index / SERIES_SLOTS) % SERIES_DASHES.length],
    })
  })
  return styles
}
