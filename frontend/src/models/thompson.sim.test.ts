/**
 * The Thompson model, run through simulated hours in synthetic worlds.
 *
 * The unit tests pin what the model does with one decision. These ask whether the whole thing works
 * when results arrive a minute late and payouts move: does it earn, does it need its forgetting,
 * does it drop a network that turns stingy and find it again when it turns generous.
 *
 * Read syntheticWorld.ts before reading anything into the numbers. The worlds are invented, one of
 * them in the shape of the real feed history, so a pass here says the mechanism works. It says
 * nothing about what the real service will pay.
 *
 * "Captured" is what the model's choices paid as a share of what the best eligible network would
 * have paid on every click, which no real run can know. The control in most tests is the same model
 * with its forgetting switched off: if that control did just as well, the forgetting would not be
 * what the test is showing.
 *
 * Everything is seeded, so these are exact and repeatable. The thresholds sit well inside what the
 * runs produce: they are there to catch the model breaking, not to certify a score.
 */
import { describe, expect, it } from 'vitest'
import { seededRandom } from '../lib/random'
import { DEFAULT_BELIEF_CONFIG } from './belief'
import type { OptimizationModel } from './model'
import { randomOptimizationModel } from './random'
import { mixedWorld, observedWorld, shiftingWorld, simulate, type SyntheticWorld } from './syntheticWorld'
import { thompsonSamplingModel } from './thompson'

const MINUTE = 60_000
const HOUR = 60 * MINUTE
const SEEDS = [1, 2, 3, 4, 5, 6]
const NO_FORGETTING = { ...DEFAULT_BELIEF_CONFIG, relaxationMinutes: 0 }

const thompson = (seed: number) => thompsonSamplingModel({ random: seededRandom(seed) })
const frozen = (seed: number) => thompsonSamplingModel({ random: seededRandom(seed), config: NO_FORGETTING })
const control = (seed: number) => randomOptimizationModel(seededRandom(seed))

/** Average share captured over the seeds, each seed in its own world of that kind. */
function captured(
  world: (seed: number, durationMs: number) => SyntheticWorld,
  model: (seed: number) => OptimizationModel,
  durationMs: number,
): number {
  const shares = SEEDS.map((seed) => simulate(world(seed, durationMs), model(seed), { durationMs, seed }).captured)
  return shares.reduce((sum, share) => sum + share, 0) / shares.length
}

describe('in a world built from the docs', () => {
  it('earns far more than the random control, and most of what was there to earn', () => {
    // These runs come out near 0.94 against 0.63.
    const earned = captured(mixedWorld, thompson, HOUR)

    expect(earned).toBeGreaterThan(0.9)
    expect(earned - captured(mixedWorld, control, HOUR)).toBeGreaterThan(0.2)
  })

  it('spends a modest share of clicks exploring', () => {
    const shares = SEEDS.map(
      (seed) => simulate(mixedWorld(seed, HOUR), thompson(seed), { durationMs: HOUR, seed }).exploratoryShare,
    )
    const share = shares.reduce((sum, value) => sum + value, 0) / shares.length

    // Too little and it never notices a network improving; too much and it is paying for
    // information it already has. These runs come out near 0.10.
    expect(share).toBeGreaterThan(0.03)
    expect(share).toBeLessThan(0.25)
  })

  it('needs its forgetting where payouts keep shifting', () => {
    // Two hours, because a model that never forgets starts out fine and falls behind as the world
    // moves away from what it learned. These runs come out near 0.92 against 0.77.
    expect(captured(shiftingWorld, thompson, 2 * HOUR) - captured(shiftingWorld, frozen, 2 * HOUR)).toBeGreaterThan(0.1)
  })
})

describe('in a world shaped like the real feed history', () => {
  // Five high-paying networks take turns being generous and stingy every few minutes. This is hard
  // for any model: a network that turns stingy keeps receiving clicks for the minute it takes to
  // find out. These runs come out near 0.81 for Thompson, 0.39 for random and 0.55 without forgetting.
  it('earns about twice what the random control does', () => {
    const earned = captured(observedWorld, thompson, HOUR)

    expect(earned).toBeGreaterThan(0.75)
    expect(earned - captured(observedWorld, control, HOUR)).toBeGreaterThan(0.3)
  })

  it('falls far behind without its forgetting, which is how the first version failed here', () => {
    // Without forgetting, each swinging network is written off the first time it is seen stingy
    // and never looked at again.
    expect(captured(observedWorld, thompson, HOUR) - captured(observedWorld, frozen, HOUR)).toBeGreaterThan(0.15)
  })
})

/** Share of the decisions between two minutes of a half-hour run that went to one network. */
function shareTo(
  world: SyntheticWorld,
  networkId: string,
  seed: number,
  fromMinute: number,
  toMinute: number,
  model: (seed: number) => OptimizationModel = thompson,
): number {
  const { decisions } = simulate(world, model(seed), { durationMs: 30 * MINUTE, seed })
  const window = decisions.filter(
    (decision) => decision.at >= fromMinute * MINUTE && decision.at < toMinute * MINUTE,
  )
  return window.filter((decision) => decision.networkId === networkId).length / window.length
}

describe('when a steady network that was written off improves', () => {
  /**
   * Two networks advertising the same rate. "steady" always pays 0.40. "late" pays 0.30 for ten
   * minutes, long enough to be written off, and 0.60 from then on.
   */
  const world: SyntheticWorld = {
    merchantIds: [1],
    networks: [
      {
        network: { id: 'steady', name: 'Steady', advertisedRate: 0.4, supportedMerchantIds: [1] },
        payouts: [0.4],
        shiftsAt: [],
        noiseFraction: 0.02,
      },
      {
        network: { id: 'late', name: 'Late', advertisedRate: 0.4, supportedMerchantIds: [1] },
        payouts: [0.3, 0.6],
        shiftsAt: [10 * MINUTE],
        noiseFraction: 0.02,
      },
    ],
  }

  it('finds it again, because doubt about an unseen network comes back with time', () => {
    for (const seed of SEEDS) expect(shareTo(world, 'late', seed, 20, 30)).toBeGreaterThan(0.8)
  })

  it('never does without forgetting', () => {
    for (const seed of SEEDS) expect(shareTo(world, 'late', seed, 20, 30, frozen)).toBeLessThan(0.05)
  })
})

describe('when a network swings between generous and stingy', () => {
  /**
   * The situation the real service puts the model in. "swinger" pays 0.58 for five minutes, 0.04
   * for the next ten, then 0.58 again. "steady" always pays 0.30.
   */
  const world: SyntheticWorld = {
    merchantIds: [1],
    networks: [
      {
        network: { id: 'steady', name: 'Steady', advertisedRate: 0.3, supportedMerchantIds: [1] },
        payouts: [0.3],
        shiftsAt: [],
        noiseFraction: 0.02,
      },
      {
        network: { id: 'swinger', name: 'Swinger', advertisedRate: 0.6, supportedMerchantIds: [1] },
        payouts: [0.58, 0.04, 0.58],
        shiftsAt: [5 * MINUTE, 15 * MINUTE],
        noiseFraction: 0.02,
      },
    ],
  }

  it('routes to it while it is generous', () => {
    for (const seed of SEEDS) expect(shareTo(world, 'swinger', seed, 1, 5)).toBeGreaterThan(0.9)
  })

  it('drops it within two minutes of it turning stingy, bar the odd look', () => {
    // It turns stingy at minute 5. The first results saying so arrive a minute later.
    for (const seed of SEEDS) expect(shareTo(world, 'swinger', seed, 7, 15)).toBeLessThan(0.1)
  })

  it('finds it again after it turns generous, which the first version never did', () => {
    // Generous again from minute 15. Those odd looks are what notice.
    for (const seed of SEEDS) {
      expect(shareTo(world, 'swinger', seed, 20, 30)).toBeGreaterThan(0.8)
      expect(shareTo(world, 'swinger', seed, 20, 30, frozen)).toBeLessThan(0.05)
    }
  })
})
