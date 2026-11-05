/**
 * Thompson sampling: route each click to the network with the highest plausible payout.
 *
 * The model holds a belief about every network (belief.ts): an estimate of what it pays per click
 * and how sure that estimate is. To decide a click it draws one plausible payout from the belief of
 * each network that covers the merchant, and routes to the highest draw.
 *
 * That one rule does both jobs. A network it is sure about draws close to its estimate, so the best
 * known network usually wins: that is earning. A network it has not seen for a while, and that is
 * known to vary, draws from a wide range, so now and then it wins on a high draw and gets a click:
 * that is learning. A steady network far behind is left alone, and a network that swings between
 * generous and stingy is looked at again every few minutes.
 *
 * Three properties matter for this problem in particular.
 *
 *   The draws differ from click to click. About twenty decisions are always made before their
 *   results arrive, and a rule without randomness would send all twenty to the same place.
 *
 *   Clicks already on their way count. A result in transit will settle part of the doubt about its
 *   network whatever it turns out to say, so until it lands the model draws for that network as if
 *   that doubt were already gone. Without this it keeps sending speculative clicks to a network it
 *   has just tried, for a whole minute, to learn something it is about to be told anyway.
 *
 *   Merchants carry no hidden information (CONCEPTS.md), so there is one belief per network and
 *   every result teaches it, whichever merchant the click was for and whichever model routed it.
 */
import type { Network } from '../api/client'
import { rate } from '../lib/format'
import { standardNormal } from '../lib/random'
import {
  beliefAt,
  beliefParams,
  DEFAULT_BELIEF_CONFIG,
  observe as foldResult,
  priorBelief,
  type Belief,
  type BeliefConfig,
  type BeliefParams,
} from './belief'
import { eligibleNetworks, type OptimizationModel } from './model'

/** Until a result says otherwise. API.md: commissions are disclosed one minute after routing. */
const DEFAULT_DISCLOSURE_DELAY_MS = 60_000

/**
 * How long past the disclosure delay a result is still taken to be on its way.
 *
 * A click is decided when it is issued, but its delay runs from when it is routed, which can be a
 * few seconds later if the routing call had to be retried, and the result is then only read at the
 * next poll. Without this allowance a click stops counting as on its way a moment before its result
 * lands, and for that moment the network's doubt is back as if nothing had been tried.
 */
export const PENDING_GRACE_MS = 10_000

/** One network's belief at a given moment, as an operator would read it. */
export interface NetworkBeliefView {
  networkId: string
  /** What the model thinks the network pays per click at the moment asked about. */
  estimate: number
  /** One standard deviation of doubt around that estimate. */
  uncertainty: number
  /** The standard deviation used for draws, after counting results still on their way. */
  samplingUncertainty: number
  /** This model's clicks whose results are still expected at the moment asked about. */
  inFlight: number
  /** What the network usually pays, across all of its results. */
  usual: number
  /** How much that usually varies, as one standard deviation. */
  usualSpread: number
  /** `routedAt` of the newest result behind the estimate. Null: none yet, the estimate is the claim. */
  newestResultAt: number | null
  observations: number
}

export interface ThompsonModel extends OptimizationModel {
  /** What the model believes about every network at `now`. */
  beliefs(now: number): NetworkBeliefView[]
}

export interface ThompsonOptions {
  /** Injected so a test can pin the draws. Two uniforms are consumed per eligible network. */
  random?: () => number
  config?: BeliefConfig
}

interface Candidate {
  network: Network
  observations: number
  /** The estimate at decision time. */
  estimate: number
  /** One standard deviation of doubt at decision time, after counting results on their way. */
  uncertainty: number
  inFlight: number
  draw: number
}

function evidence(candidate: Candidate): string {
  const estimate = `${rate(candidate.estimate)} ± ${rate(candidate.uncertainty)}`
  const { observations, inFlight } = candidate
  const seen =
    observations === 0
      ? 'advertised, no results yet'
      : `after ${observations} result${observations === 1 ? '' : 's'}`
  return inFlight === 0 ? `${estimate} (${seen})` : `${estimate} (${seen}, ${inFlight} on the way)`
}

/** A claim that is not a number says nothing, so that network starts from the typical rate instead. */
function claimOf(network: Network, params: BeliefParams): number {
  return Number.isFinite(network.advertisedRate) ? network.advertisedRate : params.scale
}

/** Pending results reduce doubt, but cannot say which way the estimate will move. */
function samplingUncertainty(variance: number, inFlight: number, noiseVariance: number): number {
  return Math.sqrt(inFlight === 0 ? variance : 1 / (1 / variance + inFlight / noiseVariance))
}

export function thompsonSamplingModel(options: ThompsonOptions = {}): ThompsonModel {
  const random = options.random ?? Math.random
  const config = options.config ?? DEFAULT_BELIEF_CONFIG

  let params: BeliefParams | null = null
  const beliefs = new Map<string, Belief>()
  /**
   * This model's own clicks to each network whose results are awaited: click id to decision time.
   * A click leaves when its result is observed, or by age if no result ever comes.
   */
  const awaited = new Map<string, Map<string, number>>()
  let disclosureDelayMs = DEFAULT_DISCLOSURE_DELAY_MS

  /** Starts every network at its own advertised claim, held loosely. */
  function startOver(networks: readonly Network[]): BeliefParams {
    const fresh = beliefParams(networks, config)
    beliefs.clear()
    awaited.clear()
    for (const network of networks) {
      beliefs.set(network.id, priorBelief(claimOf(network, fresh), fresh))
    }
    params = fresh
    return fresh
  }

  /** A network that was not in the prepared field starts from its claim like any other. */
  function beliefFor(network: Network, current: BeliefParams): Belief {
    const known = beliefs.get(network.id)
    if (known !== undefined) return known
    const prior = priorBelief(claimOf(network, current), current)
    beliefs.set(network.id, prior)
    return prior
  }

  /**
   * Whether a click decided at `decidedAt` and not yet answered is still worth waiting for at `now`.
   *
   * The age limit is for the click that was never routed, or whose result never comes: it must not
   * hold the network's doubt down forever.
   */
  function stillAwaited(decidedAt: number, now: number): boolean {
    return now - decidedAt < disclosureDelayMs + PENDING_GRACE_MS
  }

  /**
   * How many of this model's clicks to a network are still waiting for their result at `now`.
   *
   * The one rule for what counts as waiting, shared by deciding and inspecting. It reads the record
   * and leaves it alone, so inspecting any time, even a future one, cannot change a decision.
   */
  function pendingAt(networkId: string, now: number): number {
    let pending = 0
    for (const decidedAt of awaited.get(networkId)?.values() ?? []) {
      if (stillAwaited(decidedAt, now)) pending += 1
    }
    return pending
  }

  /** How many are waiting at a decision, which also forgets the ones given up on. */
  function onTheWay(networkId: string, now: number): number {
    const clicks = awaited.get(networkId)
    if (clicks === undefined) return 0
    for (const [clickId, decidedAt] of clicks) {
      if (!stillAwaited(decidedAt, now)) clicks.delete(clickId)
    }
    return clicks.size
  }

  return {
    name: 'thompson',

    prepare(networks) {
      startOver(networks)
    },

    choose({ clickId, merchantId, networks, now }) {
      const eligible = eligibleNetworks(merchantId, networks)
      if (eligible.length === 0) return null
      if (!Number.isFinite(now)) {
        throw new Error(
          `thompsonSamplingModel: asked to decide at time ${now}. Without a real time there is no ` +
            `telling how stale each belief is.`,
        )
      }

      // The loop prepares the model before the first click. Asked cold, it prepares itself from the
      // field it is being asked about, so it can be used on its own.
      const current = params ?? startOver(networks)

      const candidates = eligible.map((network): Candidate => {
        const belief = beliefFor(network, current)
        const view = beliefAt(belief, now, current)
        const inFlight = onTheWay(network.id, now)
        const uncertainty = samplingUncertainty(view.variance, inFlight, belief.noiseVariance)
        return {
          network,
          observations: belief.observations,
          estimate: view.mean,
          uncertainty,
          inFlight,
          draw: view.mean + uncertainty * standardNormal(random),
        }
      })
      const chosen = candidates.reduce((best, candidate) => (candidate.draw > best.draw ? candidate : best))
      const favorite = candidates.reduce((best, candidate) =>
        candidate.estimate > best.estimate ? candidate : best,
      )

      const clicks = awaited.get(chosen.network.id)
      if (clicks === undefined) awaited.set(chosen.network.id, new Map([[clickId, now]]))
      else clicks.set(clickId, now)

      if (chosen === favorite) {
        return {
          networkId: chosen.network.id,
          rationale:
            candidates.length === 1
              ? `only network covering this merchant: ${evidence(chosen)}`
              : `favorite of ${candidates.length}: ${evidence(chosen)}`,
          exploratory: false,
        }
      }
      return {
        networkId: chosen.network.id,
        rationale:
          `exploring: drew ${rate(chosen.draw)} from ${evidence(chosen)}; favorite ` +
          `${favorite.network.name} at ${rate(favorite.estimate)} drew ${rate(favorite.draw)}`,
        exploratory: true,
      }
    },

    observe(observation) {
      const delay = observation.disclosedAt - observation.routedAt
      // The service says how late results are by being that late. One minute on the real one.
      if (Number.isFinite(delay) && delay > 0) disclosureDelayMs = delay

      // The result is here, so the click is no longer on its way. Only this model's own clicks are
      // in the record: a result for anyone else's click finds nothing to remove.
      awaited.get(observation.networkId)?.delete(observation.clickId)

      const belief = beliefs.get(observation.networkId)
      // Before the field is known there is no scale to weigh a result against, and a network that
      // is not in the field can never be chosen. Either way there is nothing to update.
      if (params === null || belief === undefined) return
      beliefs.set(
        observation.networkId,
        foldResult(belief, observation.commission, observation.routedAt, params),
      )
    },

    beliefs(now) {
      const current = params
      if (current === null) return []
      return Array.from(beliefs, ([networkId, belief]) => {
        const view = beliefAt(belief, now, current)
        const inFlight = pendingAt(networkId, now)
        return {
          networkId,
          estimate: view.mean,
          uncertainty: Math.sqrt(view.variance),
          samplingUncertainty: samplingUncertainty(view.variance, inFlight, belief.noiseVariance),
          inFlight,
          usual: belief.usualMean,
          usualSpread: Math.sqrt(belief.usualVariance),
          newestResultAt: belief.asOf,
          observations: belief.observations,
        }
      })
    },
  }
}
