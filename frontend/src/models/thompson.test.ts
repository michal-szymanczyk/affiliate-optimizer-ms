/**
 * The Thompson sampling model.
 *
 * Its choices are random by design, so these tests pin the randomness: `drawing(...)` builds a
 * random source whose normal draws come out as chosen values. With the draws fixed, each test can
 * say exactly which network should win and why.
 *
 * The group that matters most is "forgetting with time". The SAME draw loses for a network seen a
 * minute ago and wins for the same network half an hour later, but only if that network is known to
 * swing: a steady network that is behind stays behind however long ago it was seen. That is what
 * lets the model keep an eye on the networks worth watching without paying to watch the rest.
 */
import { describe, expect, it } from 'vitest'
import { seededRandom } from '../lib/random'
import type { DecisionContext, SettledObservation } from './model'
import { thompsonSamplingModel } from './thompson'
import type { Network } from '../api/client'

const T0 = 1_700_000_000_000
const MINUTE = 60_000

// Median advertised rate 0.4, so at the defaults: noise sd 0.02, stale knowledge relaxes over ten
// minutes, and each claim starts with a doubt of half itself: 0.2 for Alpha, 0.3 for Bravo.
const NETWORKS: Network[] = [
  { id: 'net-a', name: 'Alpha', advertisedRate: 0.4, supportedMerchantIds: [1, 2, 3] },
  { id: 'net-b', name: 'Bravo', advertisedRate: 0.6, supportedMerchantIds: [3, 4] },
  { id: 'net-c', name: 'Charlie', advertisedRate: 0.2, supportedMerchantIds: [9] },
]

function click(merchantId: number, now = T0, networks: readonly Network[] = NETWORKS): DecisionContext {
  return { merchantId, networks, now }
}

function result(networkId: string, commission: number, routedAt = T0, delay = MINUTE): SettledObservation {
  return { clickId: `clk_${networkId}_${routedAt}`, networkId, commission, routedAt, disclosedAt: routedAt + delay }
}

/**
 * A random source whose normal draws come out as the given values, in order.
 *
 * Each draw consumes two uniforms (see standardNormal): the first sets the size, the second the
 * sign. Networks are drawn for in the order they appear in the network list.
 */
function drawing(...draws: number[]): () => number {
  const uniforms = draws.flatMap((draw) => [1 - Math.exp(-(draw * draw) / 2), draw >= 0 ? 0 : 0.5])
  let next = 0
  return () => {
    const value = uniforms[next]
    next += 1
    if (value === undefined) throw new Error('scripted random source ran out of draws')
    return value
  }
}

describe('eligibility', () => {
  it('never chooses a network that does not cover the merchant', () => {
    const model = thompsonSamplingModel({ random: seededRandom(1) })

    for (let i = 0; i < 500; i += 1) {
      expect(['net-a', 'net-b']).toContain(model.choose(click(3))!.networkId)
      expect(model.choose(click(9))!.networkId).toBe('net-c')
    }
  })

  it('returns null rather than routing when nothing covers the merchant', () => {
    const model = thompsonSamplingModel({ random: () => 0 })
    expect(model.choose(click(77))).toBeNull()
    expect(model.choose(click(1, T0, []))).toBeNull()
  })

  it('says so when there was no choice to make', () => {
    const decision = thompsonSamplingModel({ random: () => 0 }).choose(click(1))

    expect(decision).toMatchObject({ networkId: 'net-a', exploratory: false })
    expect(decision!.rationale).toMatch(/only network covering this merchant/)
  })
})

describe('before any result', () => {
  it('goes on the advertised rate when every draw is neutral', () => {
    // A random source of 0 makes every draw land exactly on its estimate, so the highest claim wins.
    const decision = thompsonSamplingModel({ random: () => 0 }).choose(click(3))

    expect(decision).toMatchObject({ networkId: 'net-b', exploratory: false })
    expect(decision!.rationale).toMatch(/favorite of 2: \$0\.6000 ± \$0\.3000 \(advertised, no results yet\)/)
  })

  it('favors the higher claim without trusting it outright', () => {
    // A claim is a starting guess held loosely, so the lower claim still wins a real share of first
    // decisions. A fresh model each time: this is about the very first click. Expected about 71 / 29.
    const random = seededRandom(11)
    let higherClaim = 0
    for (let i = 0; i < 2_000; i += 1) {
      if (thompsonSamplingModel({ random }).choose(click(3))!.networkId === 'net-b') higherClaim += 1
    }

    expect(higherClaim / 2_000).toBeGreaterThan(0.64)
    expect(higherClaim / 2_000).toBeLessThan(0.78)
  })

  it('explores when a network draws above the favorite, and says which favorite it passed over', () => {
    // Alpha draws two standard deviations high: 0.4 + 2 x 0.2 = 0.8, against Bravo's 0.6.
    const decision = thompsonSamplingModel({ random: drawing(2, 0) }).choose(click(3))

    expect(decision).toMatchObject({ networkId: 'net-a', exploratory: true })
    expect(decision!.rationale).toMatch(/exploring: drew \$0\.8000 from \$0\.4000 ± \$0\.2000/)
    expect(decision!.rationale).toMatch(/favorite Bravo at \$0\.6000 drew \$0\.6000/)
  })

  it('uses exactly one draw per eligible network, in list order', () => {
    const model = thompsonSamplingModel({ random: drawing(0, 0) })

    expect(model.choose(click(3))).not.toBeNull()
    expect(() => model.choose(click(3))).toThrow(/ran out of draws/)
  })
})

describe('learning from results', () => {
  it('lets one result outweigh the claim', () => {
    const model = thompsonSamplingModel({ random: () => 0 })
    model.prepare!(NETWORKS)
    // Bravo advertises 0.6 and is seen paying 0.3. Alpha's untested 0.4 now looks better.
    model.observe!(result('net-b', 0.3))

    const decision = model.choose(click(3, T0 + MINUTE))
    expect(decision).toMatchObject({ networkId: 'net-a', exploratory: false })
  })

  it('learns about a network from clicks for any merchant, and only about that network', () => {
    const model = thompsonSamplingModel({ random: () => 0 })
    model.prepare!(NETWORKS)
    model.observe!(result('net-b', 0.3))

    const beliefs = new Map(model.beliefs(T0).map((belief) => [belief.networkId, belief]))
    expect(beliefs.get('net-b')).toMatchObject({ observations: 1, newestResultAt: T0 })
    expect(beliefs.get('net-b')!.estimate).toBeCloseTo(0.301, 3)
    // The claim counts as two results, so one result at 0.3 puts "usually" at 0.5.
    expect(beliefs.get('net-b')!.usual).toBeCloseTo(0.5, 12)
    expect(beliefs.get('net-a')).toMatchObject({ estimate: 0.4, usual: 0.4, observations: 0, newestResultAt: null })
  })

  it('ignores a result it cannot place instead of failing on it', () => {
    const cold = thompsonSamplingModel({ random: () => 0 })
    // Before the field is known there is nothing to weigh a result against.
    expect(() => cold.observe!(result('net-a', 0.5))).not.toThrow()
    expect(cold.beliefs(T0)).toEqual([])

    cold.prepare!(NETWORKS)
    expect(() => cold.observe!(result('net-unknown', 0.5))).not.toThrow()
    expect(cold.beliefs(T0).map((belief) => belief.networkId)).toEqual(['net-a', 'net-b', 'net-c'])
  })

  it('starts over when it is prepared again', () => {
    const model = thompsonSamplingModel({ random: () => 0 })
    model.prepare!(NETWORKS)
    model.observe!(result('net-b', 0.3))
    model.prepare!(NETWORKS)

    expect(model.beliefs(T0).every((belief) => belief.observations === 0)).toBe(true)
  })

  it('prepares itself from the field when asked to decide cold', () => {
    const model = thompsonSamplingModel({ random: () => 0 })

    expect(model.choose(click(3))!.networkId).toBe('net-b')
    expect(model.beliefs(T0)).toHaveLength(3)
  })
})

/**
 * A model with a history: Alpha pays a steady 0.5 throughout, and Bravo pays whatever `bravo` says,
 * one result from each every three seconds. `last` is when the newest result was routed.
 */
function withHistory(
  random: () => number,
  count: number,
  bravo: (index: number) => number,
  delay = MINUTE,
) {
  const model = thompsonSamplingModel({ random })
  model.prepare!(NETWORKS)
  for (let i = 0; i < count; i += 1) {
    model.observe!(result('net-a', 0.5, T0 + i * 3_000, delay))
    model.observe!(result('net-b', bravo(i), T0 + i * 3_000, delay))
  }
  return { model, last: T0 + (count - 1) * 3_000 }
}

/** Bravo seen both generous (0.55) and stingy (0.05), ten results in all, stingy last. */
const swinging = (random: () => number, delay = MINUTE) =>
  withHistory(random, 10, (index) => (index % 2 === 0 ? 0.55 : 0.05), delay)

/** Bravo seen paying a steady 0.3, half of what it advertises, a hundred times over. */
const steady = (random: () => number) => withHistory(random, 100, () => 0.3)

describe('forgetting with time', () => {
  it('keeps a network that was low a minute ago from winning on a high draw', () => {
    // Alpha draws neutral, Bravo two standard deviations high. A minute after being seen at 0.05,
    // Bravo's doubt is still small enough that even that reaches only about 0.41 against 0.50.
    const { model, last } = swinging(drawing(0, 2))

    expect(model.choose(click(3, last + MINUTE))).toMatchObject({ networkId: 'net-a', exploratory: false })
  })

  it('lets the same draw win half an hour later, because the network is known to swing', () => {
    // Same draws. Nothing was learned in between: only time passed. Bravo has been seen at 0.55
    // before, so after half an hour it may well be there again, and it is worth one click to see.
    const { model, last } = swinging(drawing(0, 2))
    const decision = model.choose(click(3, last + 30 * MINUTE))

    expect(decision).toMatchObject({ networkId: 'net-b', exploratory: true })
    expect(decision!.rationale).toMatch(/favorite Alpha/)
  })

  it('leaves a steady network that is behind alone, however long ago it was seen', () => {
    // The same high draw, an hour on. Bravo has only ever paid 0.3, so there is little reason to
    // think it pays more now, and no click is spent finding out.
    const { model, last } = steady(drawing(0, 2))

    expect(model.choose(click(3, last + 60 * MINUTE))).toMatchObject({ networkId: 'net-a', exploratory: false })
  })

  it('relaxes toward what the network has paid, not toward what it advertises', () => {
    // An hour on, a neutral draw for Bravo sits near the 0.3 it has always paid, a long way from
    // the 0.6 it advertises.
    const { model, last } = steady(() => 0)
    const bravo = model.beliefs(last + 60 * MINUTE).find((belief) => belief.networkId === 'net-b')!

    expect(bravo.estimate).toBeGreaterThan(0.29)
    expect(bravo.estimate).toBeLessThan(0.33)
    expect(model.choose(click(3, last + 60 * MINUTE))).toMatchObject({ networkId: 'net-a', exploratory: false })
  })

  it('reports doubt that grows back toward how much each network usually varies', () => {
    const { model, last } = swinging(() => 0)
    const bravoAt = (now: number) => model.beliefs(now).find((belief) => belief.networkId === 'net-b')!

    expect(bravoAt(last + MINUTE).uncertainty).toBeLessThan(0.15)
    expect(bravoAt(last + 30 * MINUTE).uncertainty).toBeGreaterThan(0.25)
    // And no further than that, however long the wait.
    expect(bravoAt(last + 7 * 24 * 60 * MINUTE).uncertainty).toBeCloseTo(bravoAt(last).usualSpread, 6)
  })
})

describe('clicks already on their way', () => {
  it('exposes the draw uncertainty while preserving the unadjusted belief uncertainty', () => {
    const model = thompsonSamplingModel({ random: () => 0 })
    model.prepare!(NETWORKS)
    const initial = model.beliefs(T0).find((belief) => belief.networkId === 'net-a')!
    expect(initial).toMatchObject({ uncertainty: 0.2, samplingUncertainty: 0.2, inFlight: 0 })
    model.choose(click(1))
    const pending = model.beliefs(T0 + 3_000).find((belief) => belief.networkId === 'net-a')!
    expect(pending.uncertainty).toBe(initial.uncertainty)
    expect(pending.inFlight).toBe(1)
    expect(pending.samplingUncertainty).toBeCloseTo(Math.sqrt(1 / (1 / 0.2 ** 2 + 1 / 0.02 ** 2)), 12)
    const decision = model.choose(click(1, T0 + 3_000))!
    expect(decision.rationale).toContain(`± $${pending.samplingUncertainty.toFixed(4)}`)
    const expired = model.beliefs(T0 + MINUTE + 3_000).find((belief) => belief.networkId === 'net-a')!
    expect(expired.inFlight).toBe(0)
    expect(expired.samplingUncertainty).toBe(expired.uncertainty)
  })

  it('uses the learned disclosure delay in distribution inspection', () => {
    const model = thompsonSamplingModel({ random: () => 0 })
    model.prepare!(NETWORKS)
    model.observe!(result('net-b', 0.3, T0, 8_000))
    model.choose(click(1))
    expect(model.beliefs(T0 + 7_999).find((belief) => belief.networkId === 'net-a')!.inFlight).toBe(1)
    expect(model.beliefs(T0 + 8_000).find((belief) => belief.networkId === 'net-a')!.inFlight).toBe(0)
  })

  it('counts the same pending results when inspecting as when deciding, even out of order', () => {
    // Two clicks decided out of time order. A minute and five seconds on, only the later one is
    // still awaited, and the panel and the next decision must agree on that.
    const model = thompsonSamplingModel({ random: () => 0 })
    model.prepare!(NETWORKS)
    model.choose(click(1, T0 + 10_000))
    model.choose(click(1, T0))
    const later = T0 + MINUTE + 5_000
    expect(model.beliefs(later).find((belief) => belief.networkId === 'net-a')!.inFlight).toBe(1)
    expect(model.choose(click(1, later))!.rationale).toContain('1 on the way')
  })

  it('does not change seeded decisions or pending queues when inspecting present or future times', () => {
    let inspectedDraws = 0
    let controlDraws = 0
    const inspectedRandom = seededRandom(37)
    const controlRandom = seededRandom(37)
    const inspected = thompsonSamplingModel({ random: () => { inspectedDraws += 1; return inspectedRandom() } })
    const control = thompsonSamplingModel({ random: () => { controlDraws += 1; return controlRandom() } })
    inspected.prepare!(NETWORKS)
    control.prepare!(NETWORKS)
    const history: SettledObservation[] = []
    for (let index = 0; index < 50; index += 1) {
      const now = T0 + index * 3_000
      const settled = history[index - 20]
      if (settled) { inspected.observe!(settled); control.observe!(settled) }
      const before = inspected.beliefs(now)
      inspected.beliefs(now + 60 * MINUTE)
      expect(inspected.beliefs(now)).toEqual(before)
      const decision = inspected.choose(click(3, now))!
      expect(decision).toEqual(control.choose(click(3, now)))
      expect(inspectedDraws).toBe(controlDraws)
      history.push(result(decision.networkId, index % 2 === 0 ? 0.5 : 0.3, now))
    }
  })

  it('does not send a second speculative click while the first is still on its way', () => {
    // Three decisions with identical draws, half an hour after Bravo was last seen. The first tries
    // Bravo. Three seconds later that click's result is on its way and will settle the question,
    // so the same draw no longer wins.
    const { model, last } = swinging(drawing(0, 2, 0, 2, 0, 2))
    const later = last + 30 * MINUTE

    expect(model.choose(click(3, later))).toMatchObject({ networkId: 'net-b', exploratory: true })
    expect(model.choose(click(3, later + 3_000))).toMatchObject({ networkId: 'net-a', exploratory: false })
    // A minute on, the result should have arrived. It has not, so the doubt is back.
    expect(model.choose(click(3, later + MINUTE + 1_000))).toMatchObject({ networkId: 'net-b', exploratory: true })
  })

  it('says how many results it is waiting for', () => {
    const model = thompsonSamplingModel({ random: () => 0 })

    expect(model.choose(click(1))!.rationale).not.toMatch(/on the way/)
    expect(model.choose(click(1, T0 + 3_000))!.rationale).toMatch(/advertised, no results yet, 1 on the way/)
  })

  it('learns how late results are from the results themselves', () => {
    // A service that discloses after eight seconds rather than a minute. Nine seconds after the
    // speculative click, its result is overdue and the doubt is back.
    const { model, last } = swinging(drawing(0, 2, 0, 2, 0, 2), 8_000)
    const later = last + 30 * MINUTE

    expect(model.choose(click(3, later))).toMatchObject({ networkId: 'net-b' })
    expect(model.choose(click(3, later + 3_000))).toMatchObject({ networkId: 'net-a' })
    expect(model.choose(click(3, later + 9_000))).toMatchObject({ networkId: 'net-b' })
  })
})

describe('bad input', () => {
  it('refuses to decide without a real time', () => {
    const model = thompsonSamplingModel({ random: () => 0 })
    expect(() => model.choose(click(3, Number.NaN))).toThrow(/asked to decide at time NaN/)
  })

  it('fails loudly on a random source outside [0, 1)', () => {
    const model = thompsonSamplingModel({ random: () => 1.5 })
    expect(() => model.choose(click(3))).toThrow(/must return values in \[0, 1\)/)
  })
})
