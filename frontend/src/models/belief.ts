/**
 * What the client believes one network pays per click, and how sure it is of that.
 *
 * A belief has two views of a network.
 *
 *   The CURRENT view: an estimate of what it pays right now, and a doubt about that estimate.
 *   The USUAL view: what it tends to pay across all of its own results, and how much that varies.
 *
 * Two things move them.
 *
 *   A RESULT pulls the current estimate toward what the network just paid — by a lot when the
 *   belief was unsure, by a little when it was already confident — and adds to the usual view.
 *
 *   TIME makes the current view fade into the usual one. CONCEPTS.md: "The noise is small; the
 *   underlying behavior is what moves." What a network paid ten minutes ago says less and less
 *   about what it pays now, so the estimate relaxes toward what the network usually pays and the
 *   doubt grows toward how much the network usually varies. By WALL-CLOCK time, not by a count of
 *   clicks, because the networks keep moving while the loop is paused or the tab is throttled.
 *
 * The usual view is each network's own. A network that has paid the same every time is never
 * doubted much, however long ago it was seen, and costs no clicks to keep an eye on. A network
 * that has been seen both generous and stingy gets its doubt back within minutes, so it is looked
 * at again. Nothing labels the networks: the belief learns which kind each one is from its results.
 *
 * WHY THE ESTIMATE DOES NOT SIMPLY STAY PUT. The first version only grew the doubt around the last
 * estimate. Against the real service that is fatal. Its feed history (2026-10-01) shows five
 * networks alternating every few minutes between about $0.04 and about $0.58 per click. A network
 * last seen at $0.04 would need an absurdly lucky draw to be tried again, so each one was written
 * off for good the first time it was seen low, and the model ended up below a plain epsilon-greedy.
 *
 * WHAT A STALE BELIEF RELAXES TOWARD is the network's own results, not its advertised rate. The
 * claim only seeds the usual view, counted as two results, so a network caught paying half its
 * claim is soon remembered as paying half its claim.
 *
 * Everything is anchored to `routedAt`, the moment a commission describes, not to when the feed
 * disclosed it. The minute of disclosure delay then counts as staleness like any other minute.
 *
 * THE DEFAULTS. Money amounts are fractions of one scale, the median advertised rate, because the
 * absolute size of the numbers is invented (CONCEPTS.md) and nothing here should depend on it.
 *
 *   prior        0.50   A claim may miss by half of itself, and no network starts with less doubt
 *                       than half a typical rate. The real feed shows the largest claims missing
 *                       by the most, so trusting them as much as the small ones sent the first
 *                       minute of a fresh run almost entirely to the highest claim.
 *   noise        0.05   One commission scatters about this much around the current payout.
 *   relaxation   10 min How long a network's current behavior is assumed to last. "Several times
 *                       in half an hour" (CONCEPTS.md).
 *   claim weight 2      The advertised rate counts as two results in the usual view.
 *   least spread 0.05   No network is credited with varying less than this, so one that has been
 *                       steady so far is still glanced at now and then.
 *
 * thompson.sim.test.ts runs these through synthetic worlds, including one shaped like the real
 * feed history, and its header says what those runs can and cannot show.
 */
import type { Network } from '../api/client'

const MS_PER_MINUTE = 60_000

export interface BeliefConfig {
  /** Standard deviation of the starting belief, as a fraction of the typical advertised rate. */
  priorSdFraction: number
  /** Standard deviation of one commission around the current payout, as the same fraction. */
  noiseSdFraction: number
  /** How long a network's current behavior is assumed to last. Zero or less means never forget. */
  relaxationMinutes: number
  /** How many results the advertised rate counts as, in what the network usually pays. */
  claimWeight: number
  /** The least a network is assumed to vary, as a standard deviation in the same fraction. */
  minSpreadSdFraction: number
}

export const DEFAULT_BELIEF_CONFIG: BeliefConfig = {
  priorSdFraction: 0.5,
  noiseSdFraction: 0.05,
  relaxationMinutes: 10,
  claimWeight: 2,
  minSpreadSdFraction: 0.05,
}

/** The config turned into money and milliseconds, for one particular set of networks. */
export interface BeliefParams {
  /** The typical advertised rate that the money amounts are multiples of. */
  scale: number
  priorSdFraction: number
  noiseVariance: number
  /** Null when the belief never forgets. */
  relaxationMs: number | null
  claimWeight: number
  minUsualVariance: number
}

export interface Belief {
  /** The advertised rate this belief started from. */
  claim: number
  /** The estimated payout per click, as of `asOf`. `beliefAt` gives the view at a later time. */
  mean: number
  /** How uncertain that estimate was at `asOf`. */
  variance: number
  /** `routedAt` of the newest result folded in. Null before the first one. */
  asOf: number | null
  /** How many settled results have been folded in. */
  observations: number
  /** Running totals of those results, which the usual view is computed from. */
  sum: number
  sumOfSquares: number
  /** What this network usually pays: all of its results, with the claim counted as a few more. */
  usualMean: number
  /** How much what it pays usually varies. */
  usualVariance: number
}

function median(sorted: readonly number[]): number | undefined {
  const upper = sorted[Math.floor(sorted.length / 2)]
  if (upper === undefined) return undefined
  if (sorted.length % 2 === 1) return upper
  const lower = sorted[sorted.length / 2 - 1]
  return lower === undefined ? upper : (lower + upper) / 2
}

export function beliefParams(
  networks: readonly Network[],
  config: BeliefConfig = DEFAULT_BELIEF_CONFIG,
): BeliefParams {
  const { priorSdFraction, noiseSdFraction, relaxationMinutes, claimWeight, minSpreadSdFraction } = config
  if (!(priorSdFraction > 0) || !(noiseSdFraction > 0) || !(claimWeight > 0) || !(minSpreadSdFraction >= 0)) {
    throw new Error(
      `beliefParams: prior, noise and claim weight must be positive and the least spread must not be ` +
        `negative, got prior ${priorSdFraction}, noise ${noiseSdFraction}, claim weight ${claimWeight}, ` +
        `least spread ${minSpreadSdFraction}. A zero noise would make a single result certain, and a ` +
        `zero prior would leave nothing to learn.`,
    )
  }

  // A numeric comparator: the default sort compares as text and puts 9e-5 after 0.5.
  const rates = networks
    .map((network) => network.advertisedRate)
    .filter((rate) => Number.isFinite(rate) && rate > 0)
    .sort((a, b) => a - b)
  const scale = median(rates)
  if (scale === undefined) {
    throw new Error(
      'beliefParams: no network advertises a positive rate, so there is no money scale to size ' +
        'uncertainty against.',
    )
  }

  return {
    scale,
    priorSdFraction,
    noiseVariance: (noiseSdFraction * scale) ** 2,
    relaxationMs: relaxationMinutes > 0 ? relaxationMinutes * MS_PER_MINUTE : null,
    claimWeight,
    minUsualVariance: (minSpreadSdFraction * scale) ** 2,
  }
}

/**
 * How uncertain a claim is before any result: a fraction of the claim itself, or of the typical
 * rate where the claim is smaller than that.
 */
export function priorVarianceFor(advertisedRate: number, params: BeliefParams): number {
  return (params.priorSdFraction * Math.max(advertisedRate, params.scale)) ** 2
}

/** The belief before any result: the network's own claim, held loosely. */
export function priorBelief(advertisedRate: number, params: BeliefParams): Belief {
  if (!Number.isFinite(advertisedRate)) {
    throw new Error(
      `priorBelief: the advertised rate is ${advertisedRate}. A belief that starts from a number ` +
        `that is not finite can never be corrected by results.`,
    )
  }
  const variance = priorVarianceFor(advertisedRate, params)
  return {
    claim: advertisedRate,
    mean: advertisedRate,
    variance,
    asOf: null,
    observations: 0,
    sum: 0,
    sumOfSquares: 0,
    usualMean: advertisedRate,
    usualVariance: variance,
  }
}

/**
 * How much of the current view survives a wait: 1 right after a result, falling toward 0 as the
 * result ages. The rest has faded into the usual view.
 */
function kept(waitedMs: number, params: BeliefParams): number {
  if (params.relaxationMs === null) return 1
  return Math.exp(-Math.max(0, waitedMs) / params.relaxationMs)
}

/**
 * The belief as it stands at time `at`: the current view, faded toward the usual one by however
 * long it has been since the newest result.
 */
export function beliefAt(belief: Belief, at: number, params: BeliefParams): { mean: number; variance: number } {
  if (belief.asOf === null) return { mean: belief.usualMean, variance: belief.usualVariance }
  const share = kept(at - belief.asOf, params)
  // Nothing has faded yet. Returned as it is rather than recomputed, so no rounding creeps in.
  if (share === 1) return { mean: belief.mean, variance: belief.variance }
  return {
    mean: belief.usualMean + (belief.mean - belief.usualMean) * share,
    // The doubt about a faded estimate is the network's own usual spread, to the degree it has faded.
    variance: belief.variance * share ** 2 + belief.usualVariance * (1 - share ** 2),
  }
}

/**
 * Folds one settled commission into a belief and returns the new belief.
 *
 * The current view takes one step of a Kalman filter. `gain` is how far the estimate moves toward
 * the result: near 1 when the belief had gone stale, small when results arrive every few seconds
 * and each one mostly confirms the last. The usual view then takes the result into its totals.
 */
export function observe(
  belief: Belief,
  commission: number,
  routedAt: number,
  params: BeliefParams,
): Belief {
  if (!Number.isFinite(commission) || !Number.isFinite(routedAt)) {
    throw new Error(
      `observe: a commission of ${commission} routed at ${routedAt} cannot be learned from. Folding ` +
        `it in would turn the estimate into NaN, and every later result with it.`,
    )
  }

  let before = beliefAt(belief, routedAt, params)
  let noise = params.noiseVariance
  let asOf = routedAt
  if (belief.asOf !== null && routedAt < belief.asOf) {
    // A result OLDER than what the belief already reflects. The feed discloses in routing order, so
    // this should not happen, but if it does the result still says something about the present:
    // just less, because the payout has had the time in between to move. That is extra noise on
    // this one result, and the belief stays as recent as it already was.
    before = { mean: belief.mean, variance: belief.variance }
    noise += belief.usualVariance * (1 - kept(belief.asOf - routedAt, params) ** 2)
    asOf = belief.asOf
  }

  const gain = before.variance / (before.variance + noise)
  const observations = belief.observations + 1
  const sum = belief.sum + commission
  const sumOfSquares = belief.sumOfSquares + commission ** 2

  // The usual view: the results so far, plus the claim as `claimWeight` results spread by the prior.
  const weight = params.claimWeight + observations
  const usualMean = (params.claimWeight * belief.claim + sum) / weight
  const claimScatter =
    params.claimWeight * (priorVarianceFor(belief.claim, params) + (belief.claim - usualMean) ** 2)
  const resultScatter = sumOfSquares - 2 * usualMean * sum + observations * usualMean ** 2
  const usualVariance = Math.max(params.minUsualVariance, (claimScatter + resultScatter) / weight)

  return {
    claim: belief.claim,
    mean: before.mean + gain * (commission - before.mean),
    variance: (1 - gain) * before.variance,
    asOf,
    observations,
    sum,
    sumOfSquares,
    usualMean,
    usualVariance,
  }
}
