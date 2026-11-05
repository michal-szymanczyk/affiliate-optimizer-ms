/**
 * The control: pick at random among the networks that cover the click's merchant.
 *
 * It learns nothing and ignores the advertised rate, and that is what it is for. Every eligible
 * network gets the same chance on every click, so what it earns is the baseline any other
 * optimization model has to beat, and what it observes is an even look at the whole field.
 *
 * What it does get right is ELIGIBILITY. Routing to a network that does not cover the merchant is
 * rejected and still spends the click, and coverage is public — so that mistake is avoidable, and
 * an optimization model that made it would be losing clicks to a bug rather than to the market.
 */
import { eligibleNetworks, type OptimizationModel } from './model'

/** `random` is injected so a test can pin the choice. */
export function randomOptimizationModel(random: () => number = Math.random): OptimizationModel {
  return {
    name: 'random',
    choose({ merchantId, networks }) {
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
