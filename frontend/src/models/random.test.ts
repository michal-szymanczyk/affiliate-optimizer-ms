/**
 * The shipped optimization model.
 *
 * Only one behavior here really matters, and it is not the randomness: the optimization model must
 * never choose a network that does not cover the click's merchant. Coverage is public, so that
 * mistake is avoidable — and it still spends the click.
 */
import { describe, expect, it } from 'vitest'
import { eligibleNetworks, type DecisionContext } from './model'
import { randomOptimizationModel } from './random'
import type { Network } from '../api/client'

const NETWORKS: Network[] = [
  { id: 'net-a', name: 'A', advertisedRate: 0.4, supportedMerchantIds: [1, 2, 3] },
  { id: 'net-b', name: 'B', advertisedRate: 0.6, supportedMerchantIds: [3, 4] },
  { id: 'net-c', name: 'C', advertisedRate: 0.2, supportedMerchantIds: [9] },
]

/** A click for this merchant. The random model has no use for the id or the time, so they are fixed. */
function click(merchantId: number, networks: readonly Network[] = NETWORKS): DecisionContext {
  return { clickId: 'clk_1', merchantId, networks, now: 0 }
}

describe('eligibleNetworks', () => {
  it('returns only networks covering the merchant', () => {
    expect(eligibleNetworks(3, NETWORKS).map((n) => n.id)).toEqual(['net-a', 'net-b'])
    expect(eligibleNetworks(1, NETWORKS).map((n) => n.id)).toEqual(['net-a'])
    expect(eligibleNetworks(99, NETWORKS)).toEqual([])
  })
})

describe('randomOptimizationModel', () => {
  it('never chooses a network that does not cover the merchant', () => {
    // Every draw, across the whole [0,1) range, must stay inside the eligible set.
    for (const draw of [0, 0.25, 0.5, 0.75, 0.999999]) {
      const decision = randomOptimizationModel(() => draw).choose(click(3))
      expect(decision).not.toBeNull()
      expect(['net-a', 'net-b']).toContain(decision!.networkId)
    }
  })

  it('returns null rather than routing when nothing covers the merchant', () => {
    // A real outcome, not an error. The caller lets the click lapse instead of spending it on a
    // certain rejection.
    expect(randomOptimizationModel(() => 0).choose(click(77))).toBeNull()
    expect(randomOptimizationModel(() => 0).choose(click(1, []))).toBeNull()
  })

  it('spreads across the eligible set', () => {
    const first = randomOptimizationModel(() => 0).choose(click(3))
    const last = randomOptimizationModel(() => 0.999999).choose(click(3))
    expect(first!.networkId).toBe('net-a')
    expect(last!.networkId).toBe('net-b')
  })

  it('ignores the advertised rate — that is a different optimization model', () => {
    // net-b advertises 12 against net-a's 4. A random optimization model must not prefer it.
    const chosen = new Set<string>()
    for (let i = 0; i < 200; i += 1) {
      chosen.add(randomOptimizationModel().choose(click(3))!.networkId)
    }
    expect(chosen).toEqual(new Set(['net-a', 'net-b']))
  })

  it('records why it chose, so a harness has somewhere to put its reasoning', () => {
    const decision = randomOptimizationModel(() => 0).choose(click(3))
    expect(decision!.rationale).toMatch(/random among 2 networks/)
  })

  it('fails loudly on an out-of-range random source rather than routing to undefined', () => {
    expect(() => randomOptimizationModel(() => 1.5).choose(click(3))).toThrow(/must return a value in \[0, 1\)/)
  })
})
