/**
 * The contract every optimization model implements, and the one rule they all share.
 *
 * An optimization model is a named routing strategy. The loop asks the ACTIVE model to choose a
 * network for each click, and tells EVERY model what each settled click paid, whichever model
 * routed it: an observation of what a network paid is true regardless of who asked for it.
 *
 * Time is passed in, never read from a clock. A decision is then a function of what the model has
 * been told plus its injected randomness, which is what lets a model be tested without timers and
 * driven through simulated hours in milliseconds.
 */
import type { Network } from '../api/client'

export interface DecisionContext {
  /**
   * The click being decided. The same id comes back in the `SettledObservation` of its result, so
   * a model can tell which of its own decisions a result answers.
   */
  clickId: string
  merchantId: number
  networks: readonly Network[]
  /**
   * When the decision is made, in ms on the SERVER's clock (the click's `issuedAt`).
   *
   * The server's clock because results are stamped with it too. A model that ages its knowledge
   * compares this against `routedAt` values from the feed, and mixing in the browser's clock would
   * turn any skew between the two machines into phantom staleness.
   */
  now: number
}

export interface RoutingDecision {
  networkId: string
  /** Why this network, in words an operator can read. Stored with the click. */
  rationale: string
  /**
   * True when the model knowingly passed over the network it currently rates highest, to learn.
   * Absent for a model that has no notion of a favorite.
   */
  exploratory?: boolean
}

/** One settled commission, as the feed disclosed it. */
export interface SettledObservation {
  clickId: string
  networkId: string
  commission: number
  /** When the click was committed: the moment the commission describes. */
  routedAt: number
  disclosedAt: number
}

export interface OptimizationModel {
  readonly name: string
  /**
   * Called once, after the network list is known and before any decision or observation.
   *
   * A model that learns needs somewhere to stand before its first result: this is where it sets
   * its starting beliefs. Calling it again starts the model over.
   */
  prepare?(networks: readonly Network[]): void
  /**
   * Chooses a network for a click, or null when nothing covers its merchant.
   *
   * Null is a real outcome, not an error: with the seeded field every merchant is covered by
   * something, but an optimization model must not assume that. The caller lets such a click lapse
   * rather than spending it on a certain rejection.
   */
  choose(context: DecisionContext): RoutingDecision | null
  /**
   * Told exactly once about each settled commission, in the order the feed disclosed them.
   *
   * That includes clicks another model routed and clicks an earlier page load issued. A rejection
   * carries no commission and is not an observation of what a network pays, so it never arrives
   * here.
   */
  observe?(observation: SettledObservation): void
}

export function eligibleNetworks(
  merchantId: number,
  networks: readonly Network[],
): readonly Network[] {
  return networks.filter((network) => network.supportedMerchantIds.includes(merchantId))
}
