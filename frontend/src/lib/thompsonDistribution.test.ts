import { describe, expect, it } from 'vitest'
import {
  distributionBounds, distributionPoints, formatCpcTick,
  isPointDistribution, isValidDistribution, niceTicks, normalDensity, seriesStyles, stabilizeBounds,
} from './thompsonDistribution'

describe('normal distribution graph', () => {
  it('preserves probability density and relative peak heights', () => {
    const wide = { mean: 0.4, sd: 0.2 }
    const narrow = { mean: 0.4, sd: 0.02 }
    expect(normalDensity(0.4, wide)).toBeCloseTo(1 / (0.2 * Math.sqrt(2 * Math.PI)), 12)
    expect(normalDensity(0.4, narrow) / normalDensity(0.4, wide)).toBeCloseTo(10, 12)
    expect(normalDensity(0.2, wide)).toBeCloseTo(normalDensity(0.6, wide), 12)
    const points = distributionPoints(wide)
    let area = 0
    for (let index = 1; index < points.length; index += 1) {
      const left = points[index - 1]!
      const right = points[index]!
      area += (right.x - left.x) * (left.y + right.y) / 2
    }
    expect(area).toBeCloseTo(1, 3)
  })

  it('covers every four-sigma interval, including negative tails, on shared axes', () => {
    const distributions = [{ mean: 0.2, sd: 0.1 }, { mean: 0.7, sd: 0.02 }]
    const bounds = distributionBounds(distributions)!
    expect(bounds.xMin).toBeLessThan(-0.2)
    expect(bounds.xMax).toBeGreaterThan(0.78)
    expect(bounds.yMax).toBeGreaterThan(normalDensity(0.7, distributions[1]!))
    for (const distribution of distributions) {
      for (const point of distributionPoints(distribution)) {
        expect(point.x).toBeGreaterThanOrEqual(bounds.xMin)
        expect(point.x).toBeLessThanOrEqual(bounds.xMax)
        expect(point.y).toBeLessThan(bounds.yMax)
      }
    }
  })

  it('includes a very narrow peak exactly, even in a wide field', () => {
    const narrow = { mean: 0.4321, sd: 1e-8 }
    const bounds = distributionBounds([narrow, { mean: 0.8, sd: 0.4 }])!
    const points = distributionPoints(narrow)
    expect(points.some((point) => point.x === narrow.mean)).toBe(true)
    expect(Math.max(...points.map((point) => point.y))).toBeCloseTo(normalDensity(narrow.mean, narrow), 5)
    expect(bounds.yMax).toBeGreaterThan(normalDensity(narrow.mean, narrow))
  })

  it('represents zero uncertainty as a point with finite nonempty axes', () => {
    const point = { mean: 0.4, sd: 0 }
    expect(isPointDistribution(point)).toBe(true)
    expect(distributionPoints(point)).toEqual([])
    const bounds = distributionBounds([point])!
    expect(bounds.xMin).toBeLessThan(point.mean)
    expect(bounds.xMax).toBeGreaterThan(point.mean)
    expect(Object.values(bounds).every(Number.isFinite)).toBe(true)
    expect(isPointDistribution({ mean: 0.4, sd: Number.EPSILON })).toBe(true)
  })

  it('handles empty and invalid values without broken coordinates', () => {
    const bad = [{ mean: NaN, sd: 1 }, { mean: 1, sd: Infinity }, { mean: 1, sd: -1 }, { mean: 0, sd: Number.MAX_VALUE }]
    expect(bad.every((distribution) => !isValidDistribution(distribution))).toBe(true)
    expect(distributionBounds([])).toBeNull()
    expect(distributionBounds(bad)).toBeNull()
    expect(distributionBounds([...bad, { mean: 0.4, sd: 0.2 }])).not.toBeNull()
    expect(distributionPoints(bad[0]!)).toEqual([])
  })

  it('expands immediately but retains axes through small contractions', () => {
    const previous = { xMin: -1, xMax: 1, yMax: 10 }
    expect(stabilizeBounds(previous, { xMin: -0.9, xMax: 0.9, yMax: 9 })).toEqual(previous)
    expect(stabilizeBounds(previous, { xMin: -2, xMax: 0.9, yMax: 12 })).toEqual({ xMin: -2, xMax: 1, yMax: 12 })
    expect(stabilizeBounds(previous, { xMin: -0.5, xMax: 0.5, yMax: 5 })).toEqual({ xMin: -0.5, xMax: 0.5, yMax: 5 })
  })

  it('settles in one step when the range moves sideways and narrows', () => {
    // One edge grows while the whole shrinks. Widening first would draw [0, 1.05] for a frame and
    // only then the range actually wanted.
    const next = { xMin: 0.9, xMax: 1.05, yMax: 10 }
    const settled = stabilizeBounds({ xMin: 0, xMax: 1, yMax: 10 }, next)
    expect(settled).toEqual(next)
    expect(stabilizeBounds(settled, next)).toEqual(settled)
  })

  it('produces readable money ticks without rounding close ticks to identical labels', () => {
    const ticks = niceTicks(-0.00002, 0.00002, 4)
    const labels = ticks.values.map((value) => formatCpcTick(value, ticks.step))
    expect(ticks.values).toContain(0)
    expect(new Set(labels).size).toBe(labels.length)
    expect(labels).toContain('$0.00000')
    expect(labels).toContain('-$0.00001')
    expect(formatCpcTick(0.4, 0.1)).toBe('$0.4000')
    expect(formatCpcTick(-0.6, 0.2)).toBe('-$0.6000')
    expect(formatCpcTick(1e-12, 1e-12)).toBe('$1.00e-12')
  })

  it('keeps a network’s style whatever order or subset of curves is drawn', () => {
    const ids = ['net-a', 'net-b', 'net-c']
    const styles = seriesStyles(ids)
    expect(seriesStyles(['net-c', 'net-a', 'net-b', 'net-a'])).toEqual(styles)
    expect(ids.map((id) => styles.get(id))).toEqual([
      { color: 'var(--series-1)', dash: undefined },
      { color: 'var(--series-2)', dash: undefined },
      { color: 'var(--series-3)', dash: undefined },
    ])
  })

  it('tells more networks apart than there are hues by line pattern, never by a new hue', () => {
    // The real service has fifteen. Hues derived from a hash put several of them a few degrees apart.
    const ids = Array.from({ length: 24 }, (_, index) => `net-${String(index).padStart(2, '0')}`)
    const styles = ids.map((id) => seriesStyles(ids).get(id)!)
    expect(new Set(styles.map((style) => style.color)).size).toBe(8)
    expect(new Set(styles.map((style) => `${style.color} ${style.dash}`)).size).toBe(24)
    expect(styles.slice(0, 8).every((style) => style.dash === undefined)).toBe(true)
    expect(styles[8]!.color).toBe('var(--series-1)')
    expect(styles[8]!.dash).toBeDefined()
    expect(styles[16]!.dash).not.toBe(styles[8]!.dash)
  })
})
