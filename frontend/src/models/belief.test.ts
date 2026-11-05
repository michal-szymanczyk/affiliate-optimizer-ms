/**
 * The belief about one network.
 *
 * These pin the two things that move a belief — a result, and the passing of time — and the part
 * that makes the model work against the real service: the belief learns from a network's own
 * results whether it is the steady kind or the kind that swings, and only the second kind gets its
 * doubt back while it waits.
 *
 * They also pin three ways this arithmetic goes quietly wrong: a unit slip between minutes and
 * milliseconds, a stale estimate that drifts to the advertised claim instead of to what the network
 * has actually paid, and a number that is not finite getting folded in and staying there.
 */
import { describe, expect, it } from 'vitest'
import {
  beliefAt,
  beliefParams,
  DEFAULT_BELIEF_CONFIG,
  observe,
  priorBelief,
  priorVarianceFor,
  type Belief,
  type BeliefParams,
} from './belief'
import type { Network } from '../api/client'

const T0 = 1_700_000_000_000
const MINUTE = 60_000
const DAY = 24 * 60 * MINUTE

function network(id: string, advertisedRate: number): Network {
  return { id, name: id, advertisedRate, supportedMerchantIds: [1] }
}

// Median 0.4, so at the defaults: noise sd 0.02, least spread sd 0.02, and a prior sd of half the
// claim but never less than 0.2.
const NETWORKS = [network('low', 0.2), network('mid', 0.4), network('high', 0.6)]
const PARAMS: BeliefParams = beliefParams(NETWORKS)

/** A belief that has seen these results, three seconds apart, starting at T0. */
function after(claim: number, results: readonly number[], params = PARAMS): Belief {
  return results.reduce(
    (belief, commission, index) => observe(belief, commission, T0 + index * 3_000, params),
    priorBelief(claim, params),
  )
}

describe('beliefParams', () => {
  it('sizes noise and the least spread as fractions of the median advertised rate', () => {
    expect(PARAMS.scale).toBe(0.4)
    expect(Math.sqrt(PARAMS.noiseVariance)).toBeCloseTo(0.02, 12)
    expect(Math.sqrt(PARAMS.minUsualVariance)).toBeCloseTo(0.02, 12)
  })

  it('doubts a claim by half of itself, and never by less than half a typical rate', () => {
    // The largest claims are the ones seen missing by the most, so they start with the most doubt.
    expect(Math.sqrt(priorVarianceFor(0.6, PARAMS))).toBeCloseTo(0.3, 12)
    expect(Math.sqrt(priorVarianceFor(0.4, PARAMS))).toBeCloseTo(0.2, 12)
    // A small claim is not trusted more for being small.
    expect(Math.sqrt(priorVarianceFor(0.1, PARAMS))).toBeCloseTo(0.2, 12)
  })

  it('turns the relaxation time into milliseconds', () => {
    // Pinned because a slip between minutes, seconds and milliseconds does not fail anything: it
    // just makes the model forget a thousand times too slowly or too fast.
    expect(PARAMS.relaxationMs).toBe(10 * MINUTE)
    expect(beliefParams(NETWORKS, { ...DEFAULT_BELIEF_CONFIG, relaxationMinutes: 0 }).relaxationMs).toBeNull()
  })

  it('does not depend on the absolute size of the rates', () => {
    // CONCEPTS.md: the numbers are invented. Double every rate and every uncertainty doubles with
    // it, so no decision changes.
    const doubled = beliefParams(NETWORKS.map((entry) => network(entry.id, entry.advertisedRate * 2)))

    expect(priorVarianceFor(1.2, doubled)).toBeCloseTo(priorVarianceFor(0.6, PARAMS) * 4, 12)
    expect(doubled.noiseVariance).toBeCloseTo(PARAMS.noiseVariance * 4, 12)
    expect(doubled.minUsualVariance).toBeCloseTo(PARAMS.minUsualVariance * 4, 12)
    expect(doubled.relaxationMs).toBe(PARAMS.relaxationMs)
  })

  it('takes the median numerically, and the middle pair for an even count', () => {
    // Sorted as text, 9e-5 would land after 0.5 and the median would be wrong.
    const tiny = [network('a', 0.5), network('b', 0.00009), network('c', 0.1)]
    expect(beliefParams(tiny).scale).toBe(0.1)

    const even = [network('a', 0.1), network('b', 0.2), network('c', 0.4), network('d', 0.9)]
    expect(beliefParams(even).scale).toBeCloseTo(0.3, 12)
  })

  it('refuses settings or networks that leave nothing to scale against', () => {
    expect(() => beliefParams([])).toThrow(/no network advertises a positive rate/)
    expect(() => beliefParams([network('free', 0)])).toThrow(/no network advertises a positive rate/)
    expect(() => beliefParams(NETWORKS, { ...DEFAULT_BELIEF_CONFIG, noiseSdFraction: 0 })).toThrow(
      /must be positive/,
    )
  })
})

describe('priorBelief', () => {
  it('starts at the advertised rate, held loosely, with nothing to go stale', () => {
    const belief = priorBelief(0.6, PARAMS)
    const variance = priorVarianceFor(0.6, PARAMS)

    expect(belief).toMatchObject({ mean: 0.6, variance, asOf: null, observations: 0 })
    expect(beliefAt(belief, T0, PARAMS)).toEqual({ mean: 0.6, variance })
    expect(beliefAt(belief, T0 + DAY, PARAMS)).toEqual({ mean: 0.6, variance })
  })

  it('refuses a claim that is not a number', () => {
    expect(() => priorBelief(Number.NaN, PARAMS)).toThrow(/advertised rate is NaN/)
  })
})

describe('observe', () => {
  it('moves a new belief almost all the way to the first result and makes it nearly certain', () => {
    const belief = observe(priorBelief(0.4, PARAMS), 0.3, T0, PARAMS)

    // gain = 0.04 / (0.04 + 0.0004): one result outweighs the claim a hundred to one.
    const gain = 0.04 / 0.0404
    expect(belief.mean).toBeCloseTo(0.4 + gain * (0.3 - 0.4), 12)
    expect(belief.variance).toBeCloseTo((1 - gain) * 0.04, 12)
    expect(belief.asOf).toBe(T0)
    expect(belief.observations).toBe(1)
  })

  it('averages results that arrive close together instead of chasing each one', () => {
    const steady = after(0.4, [0.3, 0.3, 0.3, 0.3, 0.3, 0.3])
    expect(steady.mean).toBeCloseTo(0.3, 2)

    // One odd result three seconds later moves the estimate less than half of the way to it.
    const jolted = observe(steady, 0.4, T0 + 6 * 3_000, PARAMS)
    expect(jolted.mean).toBeGreaterThan(steady.mean)
    expect(jolted.mean).toBeLessThan(0.35)
  })

  it('lets a result on a stale belief override it', () => {
    const fresh = observe(priorBelief(0.4, PARAMS), 0.3, T0, PARAMS)
    // A day later the old estimate is worth almost nothing next to what the network just paid.
    const later = observe(fresh, 0.55, T0 + DAY, PARAMS)

    expect(later.mean).toBeCloseTo(0.55, 2)
    expect(later.asOf).toBe(T0 + DAY)
  })

  it('counts an out-of-order result for less, and never moves the belief back in time', () => {
    const belief = observe(priorBelief(0.4, PARAMS), 0.3, T0 + 5 * MINUTE, PARAMS)
    const onTime = observe(belief, 0.5, T0 + 5 * MINUTE, PARAMS)
    const late = observe(belief, 0.5, T0, PARAMS)

    // Five minutes old when it is folded in: the payout has had that long to move since.
    expect(late.mean - belief.mean).toBeGreaterThan(0)
    expect(late.mean - belief.mean).toBeLessThan(onTime.mean - belief.mean)
    expect(late.asOf).toBe(T0 + 5 * MINUTE)
    expect(late.variance).toBeGreaterThan(0)
    expect(late.variance).toBeLessThanOrEqual(belief.variance)
  })

  it('refuses a result that is not finite rather than folding it in', () => {
    const belief = priorBelief(0.4, PARAMS)
    expect(() => observe(belief, Number.NaN, T0, PARAMS)).toThrow(/cannot be learned from/)
    expect(() => observe(belief, 0.3, Number.NaN, PARAMS)).toThrow(/cannot be learned from/)
  })
})

describe('what a network usually pays', () => {
  it('counts the advertised rate as two results', () => {
    // Claimed 0.6, seen paying 0.3: one result against the claim's two.
    expect(after(0.6, [0.3]).usualMean).toBeCloseTo((2 * 0.6 + 0.3) / 3, 12)
    // Ten results against two: the claim has all but stopped mattering.
    expect(after(0.6, Array<number>(10).fill(0.3)).usualMean).toBeCloseTo((2 * 0.6 + 10 * 0.3) / 12, 12)
  })

  it('learns that a network which always pays the same hardly varies', () => {
    const steady = after(0.4, Array<number>(50).fill(0.4))

    expect(steady.usualMean).toBeCloseTo(0.4, 12)
    expect(Math.sqrt(steady.usualVariance)).toBeLessThan(0.05)
  })

  it('learns that a network seen both generous and stingy varies a lot', () => {
    const swinging = after(0.4, Array.from({ length: 50 }, (_, index) => (index % 2 === 0 ? 0.05 : 0.55)))

    expect(swinging.usualMean).toBeCloseTo(0.3, 1)
    expect(Math.sqrt(swinging.usualVariance)).toBeGreaterThan(0.2)
  })

  it('never credits a network with varying less than the least spread', () => {
    // However many identical results, a little doubt is kept, so the network is not trusted forever.
    const veteran = after(0.4, Array<number>(2_000).fill(0.4))
    expect(veteran.usualVariance).toBe(PARAMS.minUsualVariance)
  })
})

describe('how much a network scatters', () => {
  // Results three seconds apart that land anywhere from 0.25 to 0.55: gaps of 0.1 to 0.3.
  const SCATTERED = [0.4, 0.55, 0.3, 0.5, 0.25, 0.45, 0.35, 0.55, 0.3, 0.5, 0.25, 0.4]

  it('starts at the configured noise, and stays there for a network whose results agree', () => {
    expect(priorBelief(0.4, PARAMS).noiseVariance).toBe(PARAMS.noiseVariance)
    expect(after(0.4, Array<number>(50).fill(0.4)).noiseVariance).toBe(PARAMS.noiseVariance)
    // Too few pairs to read anything from yet, however far apart they are.
    expect(after(0.4, [0.4, 0.55, 0.3]).noiseVariance).toBe(PARAMS.noiseVariance)
  })

  it('reads it from results routed moments apart', () => {
    const noisy = after(0.4, SCATTERED)

    // The median gap is 0.2, which for two results of the same payout means each scatters by
    // 0.2 / (0.6745 x sqrt 2), about 0.21. Capped at how much the network varies in total.
    expect(noisy.recentSteps).toHaveLength(SCATTERED.length - 1)
    expect(Math.sqrt(noisy.noiseVariance)).toBeGreaterThan(0.1)
    expect(noisy.noiseVariance).toBeLessThanOrEqual(noisy.usualVariance)
  })

  it('stops a result from a noisy network moving the estimate most of the way to itself', () => {
    // The same stale belief and the same surprising result. Where results are known to scatter,
    // one of them is weak evidence, and the estimate goes less than half of the way. Where they
    // are known to agree, one result all but settles it.
    const later = T0 + 30 * MINUTE
    const noisy = after(0.4, SCATTERED)
    const quiet = after(0.4, Array<number>(SCATTERED.length).fill(0.4))
    const moved = (belief: Belief) => {
      const before = beliefAt(belief, later, PARAMS).mean
      return (observe(belief, 0.6, later, PARAMS).mean - before) / (0.6 - before)
    }

    expect(moved(noisy)).toBeLessThan(0.6)
    expect(moved(noisy)).toBeGreaterThan(0)
    expect(moved(quiet)).toBeGreaterThan(0.6)
  })

  it('is not fooled by a payout that changed between two results', () => {
    // Ten results at 0.55, then ten at 0.05. One gap of 0.5 among nineteen of nothing: a network
    // that swings, not one that scatters. A mean of the gaps would call it noisy.
    const swung = after(0.4, [...Array<number>(10).fill(0.55), ...Array<number>(10).fill(0.05)])

    expect(swung.noiseVariance).toBe(PARAMS.noiseVariance)
  })

  it('reads nothing from results routed too far apart to describe the same payout', () => {
    // A minute between results: the payout may have moved in between, so the gap is not scatter.
    const sparse = SCATTERED.reduce(
      (belief, commission, index) => observe(belief, commission, T0 + index * MINUTE, PARAMS),
      priorBelief(0.4, PARAMS),
    )

    expect(sparse.recentSteps).toHaveLength(0)
    expect(sparse.noiseVariance).toBe(PARAMS.noiseVariance)
  })

  it('keeps only the newest gaps, so a network that settles down is believed again', () => {
    const settled = after(0.4, [...SCATTERED, ...Array<number>(40).fill(0.4)])

    expect(settled.recentSteps).toHaveLength(30)
    expect(settled.noiseVariance).toBe(PARAMS.noiseVariance)
  })

  it('reads nothing from an out-of-order result', () => {
    const belief = after(0.4, [0.4, 0.4])
    const late = observe(belief, 0.9, T0, PARAMS)

    expect(late.recentSteps).toEqual(belief.recentSteps)
    expect(late.lastCommission).toBe(0.4)
  })
})

describe('beliefAt', () => {
  const observed = observe(priorBelief(0.6, PARAMS), 0.3, T0, PARAMS)

  it('is the current view at the moment of the newest result, and for any time before it', () => {
    expect(beliefAt(observed, T0, PARAMS)).toEqual({ mean: observed.mean, variance: observed.variance })
    expect(beliefAt(observed, T0 - MINUTE, PARAMS)).toEqual({ mean: observed.mean, variance: observed.variance })
  })

  it('fades toward the usual view by 1/e after one relaxation time', () => {
    const share = Math.exp(-1)
    const view = beliefAt(observed, T0 + 10 * MINUTE, PARAMS)

    expect(view.mean).toBeCloseTo(observed.usualMean + (observed.mean - observed.usualMean) * share, 12)
    expect(view.variance).toBeCloseTo(
      observed.variance * share ** 2 + observed.usualVariance * (1 - share ** 2),
      12,
    )
  })

  it('has become the usual view after a long wait, and stops there', () => {
    // A tab left asleep overnight comes back unsure, not infinitely unsure.
    const view = beliefAt(observed, T0 + 7 * DAY, PARAMS)

    expect(view.mean).toBeCloseTo(observed.usualMean, 12)
    expect(view.variance).toBeCloseTo(observed.usualVariance, 12)
  })

  it('relaxes toward what the network has actually paid, not toward its advertised claim', () => {
    // Advertised 0.6, seen paying 0.3 twenty times. A day of silence does not make the claim
    // believable again: the estimate drifts to about 0.33, nowhere near 0.6.
    const caught = after(0.6, Array<number>(20).fill(0.3))
    const view = beliefAt(caught, T0 + DAY, PARAMS)

    expect(view.mean).toBeCloseTo((2 * 0.6 + 20 * 0.3) / 22, 6)
    expect(view.mean).toBeLessThan(0.35)
  })

  it('gives its doubt back to a network that swings, and not to one that is steady', () => {
    // Both were last seen an hour ago. Only the one known to vary is worth looking at again.
    const steady = after(0.4, Array<number>(50).fill(0.4))
    const swinging = after(0.4, Array.from({ length: 50 }, (_, index) => (index % 2 === 0 ? 0.05 : 0.55)))
    const hourLater = T0 + 60 * MINUTE

    expect(Math.sqrt(beliefAt(steady, hourLater, PARAMS).variance)).toBeLessThan(0.05)
    expect(Math.sqrt(beliefAt(swinging, hourLater, PARAMS).variance)).toBeGreaterThan(0.2)
  })

  it('never forgets when relaxation is switched off', () => {
    const frozen = beliefParams(NETWORKS, { ...DEFAULT_BELIEF_CONFIG, relaxationMinutes: 0 })
    const belief = observe(priorBelief(0.6, frozen), 0.3, T0, frozen)

    expect(beliefAt(belief, T0 + 7 * DAY, frozen)).toEqual({ mean: belief.mean, variance: belief.variance })
  })
})
