/**
 * A synthetic world for exercising an optimization model: invented networks whose payouts shift,
 * clicks at the real cadence, and results disclosed a minute late.
 *
 * It exists because the real service cannot be replayed and never says what an unchosen network
 * would have paid. Here both are known, so a model can be run through a simulated hour in a few
 * milliseconds and scored against the best it could possibly have done.
 *
 * IT IS NOT EVIDENCE ABOUT THE REAL SERVICE. Two of the worlds are built from the docs' description
 * alone ("some honor it, some do not, some pay well at certain times, some are genuinely noisy").
 * The third, `observedWorld`, copies the shape of what the real feed showed on one day, but how
 * long its phases last is still a guess from about forty minutes of history. What a run here can
 * show is that a model's mechanism works: that it finds the good networks, notices when they
 * change, and does not burn clicks. What it cannot show is how much a model will earn against the
 * real thing.
 *
 * A model is driven through the same contract the loop uses (prepare, choose, observe), with the
 * same clock rules, so this is also the closest thing to an end-to-end test of that contract that
 * runs without a server.
 *
 * Test support only: nothing the running client imports. It lives in `sim/` to say so.
 */
import type { Network } from '../../api/client'
import { seededRandom, standardNormal } from '../../lib/random'
import { eligibleNetworks, type OptimizationModel, type SettledObservation } from '../model'

const NETWORK_COUNT = 15
const MERCHANT_COUNT = 100
/** Most networks scatter by about 2% of what they pay; the noisy ones by 15%. */
const QUIET_NOISE = 0.02
const LOUD_NOISE = 0.15

export interface SyntheticNetwork {
  network: Network
  /** What it pays per click: `payouts[i]` applies from `shiftsAt[i - 1]` (or the start) to `shiftsAt[i]`. */
  payouts: readonly number[]
  shiftsAt: readonly number[]
  /** Scatter of one commission, as a fraction of the current payout. */
  noiseFraction: number
}

export interface SyntheticWorld {
  networks: readonly SyntheticNetwork[]
  merchantIds: readonly number[]
}

/** What the network truly pays per click at `at`, before noise. The thing no real run can see. */
export function payoutAt(entry: SyntheticNetwork, at: number): number {
  let index = 0
  for (const shift of entry.shiftsAt) {
    if (shift > at) break
    index += 1
  }
  const payout = entry.payouts[index]
  if (payout === undefined) {
    throw new Error(
      `payoutAt: "${entry.network.id}" has ${entry.shiftsAt.length} shifts but only ` +
        `${entry.payouts.length} payouts. There must be exactly one more payout than shifts.`,
    )
  }
  return payout
}

type Behavior = 'honest' | 'underpays' | 'switches' | 'noisy' | 'unrelated'

function shuffle<T>(items: T[], random: () => number): T[] {
  for (let i = items.length - 1; i > 0; i -= 1) {
    const j = Math.floor(random() * (i + 1))
    const moved = items[i] as T
    items[i] = items[j] as T
    items[j] = moved
  }
  return items
}

function buildWorld(
  seed: number,
  durationMs: number,
  behaviors: readonly Behavior[],
  meanDwellMs: number,
): SyntheticWorld {
  const random = seededRandom(seed)
  const assigned = shuffle([...behaviors], random)

  /** A payout that changes at random moments, on average every `meanDwellMs`. */
  const shifting = (next: () => number) => {
    const payouts = [next()]
    const shiftsAt: number[] = []
    let at = -meanDwellMs * Math.log(1 - random())
    while (at < durationMs) {
      shiftsAt.push(at)
      payouts.push(next())
      at += -meanDwellMs * Math.log(1 - random())
    }
    return { payouts, shiftsAt }
  }

  const schedules = assigned.map((behavior) => {
    const advertised = 0.1 + 0.5 * random()
    switch (behavior) {
      case 'honest':
        return { advertised, payouts: [advertised], shiftsAt: [], noiseFraction: QUIET_NOISE }
      case 'underpays':
        return { advertised, payouts: [advertised * 0.5], shiftsAt: [], noiseFraction: QUIET_NOISE }
      case 'noisy':
        return { advertised, payouts: [advertised * 0.9], shiftsAt: [], noiseFraction: LOUD_NOISE }
      case 'switches': {
        // Pays a little over its claim at some times and under half of it at others.
        let generous = random() < 0.5
        const schedule = shifting(() => {
          generous = !generous
          return generous ? advertised * 1.1 : advertised * 0.45
        })
        return { advertised, ...schedule, noiseFraction: QUIET_NOISE }
      }
      case 'unrelated':
        // The claim says nothing: every so often the payout is simply drawn again.
        return { advertised, ...shifting(() => 0.1 + 0.5 * random()), noiseFraction: QUIET_NOISE }
    }
  })

  // Each merchant is covered by three to six networks.
  const coverage: number[][] = schedules.map(() => [])
  const merchantIds = Array.from({ length: MERCHANT_COUNT }, (_, index) => index + 1)
  for (const merchantId of merchantIds) {
    const covering = 3 + Math.floor(random() * 4)
    const order = shuffle(Array.from({ length: schedules.length }, (_, index) => index), random)
    for (const networkIndex of order.slice(0, covering)) coverage[networkIndex]?.push(merchantId)
  }

  return {
    merchantIds,
    networks: schedules.map((schedule, index) => ({
      network: {
        id: `net-${index + 1}`,
        name: `Network ${index + 1}`,
        advertisedRate: schedule.advertised,
        supportedMerchantIds: coverage[index] ?? [],
      },
      payouts: schedule.payouts,
      shiftsAt: schedule.shiftsAt,
      noiseFraction: schedule.noiseFraction,
    })),
  }
}

/**
 * The world the docs describe: five networks that honor their claim, three that pay half of it,
 * five that switch between generous and stingy about every seven minutes, and two noisy ones.
 */
export function mixedWorld(seed: number, durationMs: number): SyntheticWorld {
  const behaviors: Behavior[] = [
    ...Array<Behavior>(5).fill('honest'),
    ...Array<Behavior>(3).fill('underpays'),
    ...Array<Behavior>(5).fill('switches'),
    ...Array<Behavior>(2).fill('noisy'),
  ]
  return buildWorld(seed, durationMs, behaviors, 400_000)
}

/**
 * A harsher world: claims are unrelated to payouts, and every network's payout is drawn afresh
 * about every ten minutes. Whatever was learned keeps going out of date.
 */
export function shiftingWorld(seed: number, durationMs: number): SyntheticWorld {
  return buildWorld(seed, durationMs, Array<Behavior>(NETWORK_COUNT).fill('unrelated'), 600_000)
}

/**
 * The fifteen networks as the real feed history showed them on 2026-10-01: what each advertises,
 * what it was seen to pay as a multiple of that, and the share of merchants it covers.
 *
 *   swings   Pays `generous` times its claim for some minutes, then `stingy` times, and back.
 *            These are the five highest claims, and when generous they all pay about $0.55-0.60.
 *   steady   Pays its claim, every time.
 *   erratic  Pays its claim on average, with individual commissions scattered widely around it.
 */
const OBSERVED: readonly {
  advertised: number
  coverage: number
  kind: 'swings' | 'steady' | 'erratic'
  generous?: number
  stingy?: number
}[] = [
  { advertised: 0.225, coverage: 0.43, kind: 'steady' },
  { advertised: 0.14, coverage: 0.55, kind: 'steady' },
  { advertised: 0.31, coverage: 0.49, kind: 'steady' },
  { advertised: 0.0975, coverage: 0.66, kind: 'steady' },
  { advertised: 0.17, coverage: 0.46, kind: 'steady' },
  { advertised: 0.6, coverage: 0.98, kind: 'swings', generous: 1.0, stingy: 0.065 },
  { advertised: 0.4875, coverage: 0.84, kind: 'swings', generous: 1.12, stingy: 0.12 },
  { advertised: 0.775, coverage: 0.94, kind: 'swings', generous: 0.74, stingy: 0.04 },
  { advertised: 0.42, coverage: 0.82, kind: 'swings', generous: 1.36, stingy: 0.17 },
  { advertised: 0.56, coverage: 0.96, kind: 'swings', generous: 1.0, stingy: 0.08 },
  { advertised: 0.1875, coverage: 0.75, kind: 'erratic' },
  { advertised: 0.13, coverage: 0.78, kind: 'erratic' },
  { advertised: 0.255, coverage: 0.64, kind: 'erratic' },
  { advertised: 0.21, coverage: 0.64, kind: 'erratic' },
  { advertised: 0.09, coverage: 0.86, kind: 'erratic' },
]
/** Individual commissions of the erratic networks scatter by about 40% of what they pay. */
const ERRATIC_NOISE = 0.4
/** The feed showed generous and stingy phases of roughly this length. The least certain number here. */
const PHASE_MINUTES = { shortest: 3, longest: 15 }

/**
 * A world with the shape of the real one: a few high-paying networks that take turns being
 * generous and stingy, in front of a field of steady and erratic low-paying ones, with most
 * networks covering most merchants.
 *
 * This is the world the first belief failed in. Each of the swinging networks was written off for
 * good the first time it was seen stingy.
 */
export function observedWorld(seed: number, durationMs: number): SyntheticWorld {
  const random = seededRandom(seed)
  const phaseMs = () =>
    (PHASE_MINUTES.shortest + (PHASE_MINUTES.longest - PHASE_MINUTES.shortest) * random()) * 60_000

  const merchantIds = Array.from({ length: MERCHANT_COUNT }, (_, index) => index + 1)
  const networks = OBSERVED.map((shape, index): SyntheticNetwork => {
    const payouts = [shape.advertised]
    const shiftsAt: number[] = []
    if (shape.kind === 'swings') {
      const levels = [shape.advertised * (shape.generous ?? 1), shape.advertised * (shape.stingy ?? 1)]
      let phase = random() < 0.5 ? 0 : 1
      payouts[0] = levels[phase] ?? shape.advertised
      // The run starts part of the way through a phase, not at the start of one.
      for (let at = phaseMs() * random(); at < durationMs; at += phaseMs()) {
        phase = 1 - phase
        shiftsAt.push(at)
        payouts.push(levels[phase] ?? shape.advertised)
      }
    }
    return {
      network: {
        id: `net-${index + 1}`,
        name: `Network ${index + 1}`,
        advertisedRate: shape.advertised,
        supportedMerchantIds: merchantIds.filter(() => random() < shape.coverage),
      },
      payouts,
      shiftsAt,
      noiseFraction: shape.kind === 'erratic' ? ERRATIC_NOISE : QUIET_NOISE,
    }
  })

  return { merchantIds, networks }
}

export interface SimulationOptions {
  durationMs: number
  /**
   * Seeds the stream of merchants and the stream of commission noise. Two models run with the same
   * world and seed are sent the same clicks, so the difference between them is their choices.
   */
  seed: number
  clickIntervalMs?: number
  disclosureDelayMs?: number
}

export interface SimulatedDecision {
  at: number
  networkId: string
  exploratory: boolean
}

export interface SimulationResult {
  decisions: readonly SimulatedDecision[]
  /**
   * What the model's choices paid as a share of what the best eligible network would have paid on
   * each click. Both are counted before noise, so luck in the commissions does not move the score.
   * A click the model let lapse while something covered it counts as earning nothing.
   */
  captured: number
  /** Share of decisions the model flagged as exploratory. */
  exploratoryShare: number
}

export function simulate(
  world: SyntheticWorld,
  model: OptimizationModel,
  options: SimulationOptions,
): SimulationResult {
  const interval = options.clickIntervalMs ?? 3_000
  const delay = options.disclosureDelayMs ?? 60_000
  const networks = world.networks.map((entry) => entry.network)
  const byId = new Map(world.networks.map((entry) => [entry.network.id, entry]))
  const merchants = seededRandom(options.seed + 1_000_003)
  const noise = seededRandom(options.seed + 2_000_003)

  // Results wait here until their disclosure time. The delay is constant, so routing order is
  // disclosure order and a simple queue is enough.
  const awaiting: SettledObservation[] = []
  let disclosed = 0
  const decisions: SimulatedDecision[] = []
  let issued = 0
  let earned = 0
  let best = 0

  model.prepare?.(networks)

  for (let at = 0; at < options.durationMs; at += interval) {
    issued += 1
    // The id the model is asked about is the id its result comes back under, as in the real loop.
    const clickId = `sim_${issued}`
    for (let next = awaiting[disclosed]; next !== undefined && next.disclosedAt <= at; next = awaiting[disclosed]) {
      model.observe?.(next)
      disclosed += 1
    }

    const merchantId = world.merchantIds[Math.floor(merchants() * world.merchantIds.length)]
    // Drawn for every click, chosen or not, so the noise stream stays the same across models.
    const scatter = standardNormal(noise)
    if (merchantId === undefined) continue

    const eligible = eligibleNetworks(merchantId, networks)
    const decision = model.choose({ clickId, merchantId, networks, now: at })
    if (eligible.length === 0) continue

    const payoutOf = (networkId: string): number => {
      const entry = byId.get(networkId)
      return entry === undefined ? 0 : payoutAt(entry, at)
    }
    // Counted before looking at the decision: a model that lets a coverable click lapse has still
    // passed up what the best network would have paid.
    best += Math.max(...eligible.map((network) => payoutOf(network.id)))
    if (decision === null) continue
    decisions.push({ at, networkId: decision.networkId, exploratory: decision.exploratory === true })

    const chosen = byId.get(decision.networkId)
    // Routing outside coverage spends the click and earns nothing, as the real service does.
    if (chosen === undefined || !chosen.network.supportedMerchantIds.includes(merchantId)) continue

    const payout = payoutAt(chosen, at)
    earned += payout
    awaiting.push({
      clickId,
      networkId: decision.networkId,
      commission: Math.max(0.0001, payout * (1 + chosen.noiseFraction * scatter)),
      routedAt: at,
      disclosedAt: at + delay,
    })
  }

  return {
    decisions,
    captured: best === 0 ? 0 : earned / best,
    exploratoryShare:
      decisions.length === 0
        ? 0
        : decisions.filter((decision) => decision.exploratory).length / decisions.length,
  }
}
