/**
 * The normal draw and the seeded generator.
 *
 * Small, but everything a learning model does at random goes through these two, and both have an
 * edge that is easy to get wrong: a uniform of exactly 0, and a generator that is only useful if
 * the same seed really does give the same run.
 */
import { describe, expect, it } from 'vitest'
import { seededRandom, standardNormal } from './random'

/** A random source that hands out the given values in order. */
function scripted(values: number[]): () => number {
  let next = 0
  return () => {
    const value = values[next]
    next += 1
    if (value === undefined) throw new Error('scripted random source ran out of values')
    return value
  }
}

describe('standardNormal', () => {
  it('draws zero when the first uniform is 0, whatever the second is', () => {
    // Math.random can return exactly 0, and a naive ln(0) would make the draw infinite.
    // Math.abs because the arithmetic lands on negative zero, which is still zero.
    expect(Math.abs(standardNormal(scripted([0, 0])))).toBe(0)
    expect(Math.abs(standardNormal(scripted([0, 0.73])))).toBe(0)
  })

  it('maps a chosen pair of uniforms to a known draw', () => {
    // Radius sqrt(-2 ln(1 - u)) is 1 at u = 1 - e^-0.5; the second uniform picks the sign.
    const radiusOne = 1 - Math.exp(-0.5)
    expect(standardNormal(scripted([radiusOne, 0]))).toBeCloseTo(1, 12)
    expect(standardNormal(scripted([radiusOne, 0.5]))).toBeCloseTo(-1, 12)
  })

  it('consumes exactly two uniforms per draw', () => {
    const source = scripted([0, 0, 0, 0])
    standardNormal(source)
    standardNormal(source)
    expect(() => standardNormal(source)).toThrow(/ran out/)
  })

  it('fails loudly on a random source outside [0, 1)', () => {
    expect(() => standardNormal(() => 1)).toThrow(/must return values in \[0, 1\)/)
    expect(() => standardNormal(() => Number.NaN)).toThrow(/must return values in \[0, 1\)/)
  })

  it('has mean 0 and standard deviation 1 over many draws', () => {
    const random = seededRandom(7)
    const draws = Array.from({ length: 20_000 }, () => standardNormal(random))
    const mean = draws.reduce((sum, draw) => sum + draw, 0) / draws.length
    const variance = draws.reduce((sum, draw) => sum + (draw - mean) ** 2, 0) / draws.length

    expect(mean).toBeCloseTo(0, 1)
    expect(Math.sqrt(variance)).toBeCloseTo(1, 1)
  })
})

describe('seededRandom', () => {
  it('gives the same sequence for the same seed and a different one for another', () => {
    const first = seededRandom(42)
    const again = seededRandom(42)
    const other = seededRandom(43)
    const take = (random: () => number) => Array.from({ length: 5 }, random)

    const sequence = take(first)
    expect(take(again)).toEqual(sequence)
    expect(take(other)).not.toEqual(sequence)
  })

  it('stays inside [0, 1)', () => {
    const random = seededRandom(1)
    for (let i = 0; i < 10_000; i += 1) {
      const value = random()
      expect(value >= 0 && value < 1).toBe(true)
    }
  })
})
