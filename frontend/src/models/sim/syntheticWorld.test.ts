/**
 * The simulator itself. A score is only worth comparing if the simulator keeps it honest, so these
 * pin the two places where it could flatter a model: letting clicks lapse, and the contract a model
 * is driven through.
 */
import { describe, expect, it } from 'vitest'
import type { DecisionContext, OptimizationModel, SettledObservation } from '../model'
import { simulate, type SyntheticWorld } from './syntheticWorld'

const MINUTE = 60_000

/** One merchant, covered by a network that pays 0.4 and one that pays 0.2. */
const WORLD: SyntheticWorld = {
  merchantIds: [1],
  networks: [
    {
      network: { id: 'good', name: 'Good', advertisedRate: 0.4, supportedMerchantIds: [1] },
      payouts: [0.4],
      shiftsAt: [],
      noiseFraction: 0.02,
    },
    {
      network: { id: 'poor', name: 'Poor', advertisedRate: 0.4, supportedMerchantIds: [1] },
      payouts: [0.2],
      shiftsAt: [],
      noiseFraction: 0.02,
    },
  ],
}

const always = (networkId: string): OptimizationModel => ({
  name: `always ${networkId}`,
  choose: () => ({ networkId, rationale: 'always the same' }),
})

describe('simulate', () => {
  it('scores a model against the best eligible network on every click', () => {
    const options = { durationMs: 10 * MINUTE, seed: 1 }

    expect(simulate(WORLD, always('good'), options).captured).toBeCloseTo(1, 12)
    expect(simulate(WORLD, always('poor'), options).captured).toBeCloseTo(0.5, 12)
  })

  it('counts a coverable click the model let lapse as earning nothing', () => {
    // Otherwise a model could score perfectly by declining every click it was unsure of.
    const abstains: OptimizationModel = { name: 'abstains', choose: () => null }
    const result = simulate(WORLD, abstains, { durationMs: 10 * MINUTE, seed: 1 })

    expect(result.decisions).toHaveLength(0)
    expect(result.captured).toBe(0)

    // Half the clicks declined, the rest sent to the best network: half of what was there.
    let asked = 0
    const everyOther: OptimizationModel = {
      name: 'every other',
      choose: () => {
        asked += 1
        return asked % 2 === 0 ? null : { networkId: 'good', rationale: 'every other click' }
      },
    }
    expect(simulate(WORLD, everyOther, { durationMs: 10 * MINUTE, seed: 1 }).captured).toBeCloseTo(0.5, 12)
  })

  it('hands a result back under the id of the click it was decided for', () => {
    // A model matches results to its own decisions by this id, as it does in the real loop.
    const contexts: DecisionContext[] = []
    const observations: SettledObservation[] = []
    const probe: OptimizationModel = {
      name: 'probe',
      choose: (context) => {
        contexts.push(context)
        return { networkId: 'good', rationale: 'always the same' }
      },
      observe: (observation) => {
        observations.push(observation)
      },
    }

    simulate(WORLD, probe, { durationMs: 3 * MINUTE, seed: 1 })

    const decidedAt = new Map(contexts.map((context) => [context.clickId, context.now]))
    expect(decidedAt.size).toBe(contexts.length)
    expect(observations.length).toBeGreaterThan(0)
    for (const observation of observations) {
      expect(decidedAt.get(observation.clickId)).toBe(observation.routedAt)
    }
  })
})
