/**
 * The shipped optimization model: pick at random among the networks that cover the click's merchant.
 *
 * This is deliberately the only optimization model, and deliberately naive at exactly this point.
 * Designing a harness that can hold several optimization models, compare them, and record which
 * one produced which run is the interesting work, and shipping our version of it would pre-empt
 * that.
 *
 * What it does get right is ELIGIBILITY. Routing to a network that does not cover the merchant is
 * rejected and still spends the click, and coverage is public — so that mistake is avoidable, and
 * an optimization model that made it would be losing clicks to a bug rather than to the market.
 */
import type { Network } from '../api/client'

export interface RoutingDecision {
  networkId: string
  /** Why this network. Recorded so a future harness has somewhere to put its reasoning. */
  rationale: string
}

export interface OptimizationModel {
  readonly name: string
  /**
   * Chooses a network for a click, or null when nothing covers its merchant.
   *
   * Null is a real outcome, not an error: with the seeded field every merchant is covered by
   * something, but an optimization model must not assume that. The caller lets such a click lapse
   * rather than spending it on a certain rejection.
   */
  choose(merchantId: number, networks: readonly Network[]): RoutingDecision | null
}

export function eligibleNetworks(
  merchantId: number,
  networks: readonly Network[],
): readonly Network[] {
  return networks.filter((network) => network.supportedMerchantIds.includes(merchantId))
}

/** `random` is injected so a test can pin the choice. */
export function randomOptimizationModel(random: () => number = Math.random): OptimizationModel {
  return {
    name: 'random',
    choose(merchantId, networks) {
      const eligible = eligibleNetworks(merchantId, networks)
      if (eligible.length === 0) return null

      const chosen = eligible[Math.floor(random() * eligible.length)]
      if (!chosen) {
        throw new Error(
          `randomOptimizationModel: drew an index outside ${eligible.length} eligible networks. The injected ` +
            `random function must return a value in [0, 1).`,
        )
      }
      return {
        networkId: chosen.id,
        rationale: `random among ${eligible.length} networks covering merchant ${merchantId}`,
      }
    },
  }
}
