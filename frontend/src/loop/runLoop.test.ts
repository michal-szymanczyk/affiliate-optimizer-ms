/**
 * The loop's optimization-model interactions, driven against a stub client.
 *
 * These are the tests worth having about the loop, because this is where a client can be quietly
 * wrong: retrying something final, forgetting a cursor, or spending a click on a certain rejection.
 * None of them need a server.
 */
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AffiliateServiceClient, type Click, type CommissionFeed, type Network } from '../api/client'
import { ApiError, TransportError } from '../api/errors'
import { ClickLog, clickState } from './clickLog'
import { RunLoop } from './runLoop'
import type { DecisionContext, OptimizationModel, SettledObservation } from '../models/model'
import { randomOptimizationModel } from '../models/random'

const T0 = 1_700_000_000_000

const NETWORKS: Network[] = [
  { id: 'net-covers', name: 'Covering Network', advertisedRate: 0.5, supportedMerchantIds: [42] },
  { id: 'net-misses', name: 'Missing Network', advertisedRate: 0.7, supportedMerchantIds: [7] },
]

/**
 * A scripted client. Each method returns the next queued response, or throws the next queued error,
 * and records what it was asked to do.
 */
function stubClient(overrides: Partial<Record<keyof AffiliateServiceClient, unknown>> = {}) {
  let issued = 0
  const routeCalls: { clickId: string; networkId: string }[] = []
  const feedCalls: (string | null)[] = []

  const client = {
    merchants: vi.fn(async () => [{ merchantId: 42, name: 'Target' }]),
    networks: vi.fn(async () => NETWORKS),
    nextClick: vi.fn(async (): Promise<Click> => {
      issued += 1
      return {
        clickId: `clk_${issued}`,
        merchantId: 42,
        issuedAt: new Date(T0).toISOString(),
        expiresAt: new Date(T0 + 120_000).toISOString(),
      }
    }),
    route: vi.fn(async (clickId: string, networkId: string) => {
      routeCalls.push({ clickId, networkId })
      return {
        outcome: 'accepted' as const,
        clickId,
        networkId,
        routedAt: new Date(T0).toISOString(),
        alreadyRouted: false,
        commissionAvailableAt: new Date(T0 + 60_000).toISOString(),
      }
    }),
    commissions: vi.fn(async (cursor: string | null): Promise<CommissionFeed> => {
      feedCalls.push(cursor)
      return { commissions: [], nextCursor: 'cursor-1' }
    }),
    ...overrides,
  }

  return { client: client as unknown as AffiliateServiceClient, routeCalls, feedCalls, raw: client }
}

function makeLoop(
  clientBundle: ReturnType<typeof stubClient>,
  log = new ClickLog(),
  models: readonly OptimizationModel[] = [randomOptimizationModel(() => 0)],
) {
  const loop = new RunLoop({
    client: clientBundle.client,
    models,
    log,
    now: () => T0,
    onChange: () => {},
  })
  return { loop, log }
}

/** A model that routes where it is told to and keeps everything the loop tells it. */
function probeModel(name: string, networkId = 'net-covers') {
  const prepared: (readonly Network[])[] = []
  const contexts: DecisionContext[] = []
  const observations: SettledObservation[] = []
  const model: OptimizationModel = {
    name,
    prepare: (networks) => {
      prepared.push(networks)
    },
    choose: (context) => {
      contexts.push(context)
      return { networkId, rationale: `${name} says so`, exploratory: true }
    },
    observe: (observation) => {
      observations.push(observation)
    },
  }
  return { model, prepared, contexts, observations }
}

function settlement(clickId: string, commission: number) {
  return {
    clickId,
    merchantId: 42,
    routedNetworkId: 'net-covers',
    routedAt: new Date(T0).toISOString(),
    kind: 'settlement' as const,
    state: 'settled' as const,
    commission,
    errorCode: null,
    disclosedAt: new Date(T0 + 60_000).toISOString(),
  }
}

function rejection(clickId: string) {
  return {
    clickId,
    merchantId: 42,
    routedNetworkId: 'net-misses',
    routedAt: new Date(T0).toISOString(),
    kind: 'rejection' as const,
    state: 'rejected' as const,
    commission: null,
    errorCode: 'MERCHANT_NOT_SUPPORTED' as const,
    disclosedAt: new Date(T0).toISOString(),
  }
}

beforeEach(() => {
  vi.useFakeTimers()
})

describe('starting up', () => {
  it('fetches reference data once and anchors the run', async () => {
    const bundle = stubClient()
    const { loop } = makeLoop(bundle)

    await loop.start()

    expect(bundle.raw.merchants).toHaveBeenCalledTimes(1)
    expect(bundle.raw.networks).toHaveBeenCalledTimes(1)
    expect(loop.state.startedAt).toBe(T0)
    expect(loop.state.running).toBe(true)
    loop.stop()
  })

  it('propagates a startup failure rather than routing blind', async () => {
    // Without coverage the optimization model cannot avoid ineligible networks, so every click
    // would be spent on a certain rejection. Failing loudly is the only sane answer.
    const bundle = stubClient({
      networks: vi.fn(async () => {
        throw new ApiError(401, 'UNAUTHORIZED', 'bad credentials')
      }),
    })
    const { loop } = makeLoop(bundle)

    await expect(loop.start()).rejects.toThrow(/bad credentials/)
    loop.stop()
  })
})

describe('routing', () => {
  it('routes each click to a network that covers its merchant', async () => {
    const bundle = stubClient()
    const { loop, log } = makeLoop(bundle)

    await loop.start()
    await vi.advanceTimersByTimeAsync(0)

    expect(bundle.routeCalls).toHaveLength(1)
    // Merchant 42 is covered by net-covers only; an optimization model that picked net-misses would
    // be spending clicks on an avoidable rejection.
    expect(bundle.routeCalls[0]?.networkId).toBe('net-covers')
    expect(log.size).toBe(1)
    expect(clickState(log.get('clk_1')!, T0)).toBe('routed')
    loop.stop()
  })

  it('records a rejection against the click and does NOT retry it', async () => {
    const bundle = stubClient({
      route: vi.fn(async () => {
        throw new ApiError(422, 'MERCHANT_NOT_SUPPORTED', 'not covered')
      }),
    })
    const { loop, log } = makeLoop(bundle)

    await loop.start()
    await vi.advanceTimersByTimeAsync(0)

    // Routing is final: the click is spent, so calling again could not undo it and would only
    // consume ration.
    expect(bundle.raw.route).toHaveBeenCalledTimes(1)
    expect(log.get('clk_1')?.errorCode).toBe('MERCHANT_NOT_SUPPORTED')
    expect(clickState(log.get('clk_1')!, T0)).toBe('rejected')
    loop.stop()
  })

  it('does not misrecord a request error as a committed route rejection', async () => {
    const bundle = stubClient({
      route: vi.fn(async () => {
        throw new ApiError(404, 'NETWORK_NOT_FOUND', 'network disappeared')
      }),
    })
    const { loop, log } = makeLoop(bundle)

    await loop.start()
    await vi.advanceTimersByTimeAsync(0)

    expect(bundle.raw.route).toHaveBeenCalledTimes(1)
    expect(log.get('clk_1')).toMatchObject({
      routedAt: null,
      routedNetworkId: null,
      errorCode: null,
    })
    expect(loop.state.feedError?.code).toBe('NETWORK_NOT_FOUND')
    loop.stop()
  })

  it('safely replays the same route after an ambiguous transport failure', async () => {
    let attempts = 0
    const route = vi.fn(async (clickId: string, networkId: string) => {
      attempts += 1
      if (attempts === 1) throw new TransportError('/route', new Error('response lost'))
      return {
        outcome: 'accepted' as const,
        clickId,
        networkId,
        routedAt: new Date(T0).toISOString(),
        alreadyRouted: true,
        commissionAvailableAt: new Date(T0 + 60_000).toISOString(),
      }
    })
    const bundle = stubClient({ route })
    const { loop, log } = makeLoop(bundle)

    await loop.start()
    await vi.advanceTimersByTimeAsync(0)
    expect(route).toHaveBeenCalledTimes(1)

    await vi.advanceTimersByTimeAsync(1_000)
    expect(route).toHaveBeenCalledTimes(2)
    expect(route.mock.calls[0]).toEqual(route.mock.calls[1])
    expect(clickState(log.get('clk_1')!, T0)).toBe('routed')
    loop.stop()
  })

  it('does not issue another route attempt when stopped during recovery backoff', async () => {
    const route = vi.fn(async () => {
      throw new TransportError('/route', new Error('response lost'))
    })
    const bundle = stubClient({ route })
    const { loop } = makeLoop(bundle)

    await loop.start()
    await vi.advanceTimersByTimeAsync(0)
    expect(route).toHaveBeenCalledTimes(1)

    loop.stop()
    await vi.advanceTimersByTimeAsync(10_000)
    expect(route).toHaveBeenCalledTimes(1)
  })

  it('lets a click lapse when no network covers its merchant', async () => {
    const bundle = stubClient({
      networks: vi.fn(async () => [
        { id: 'net-misses', name: 'Missing Network', advertisedRate: 0.7, supportedMerchantIds: [7] },
      ]),
    })
    const { loop, log } = makeLoop(bundle)

    await loop.start()
    await vi.advanceTimersByTimeAsync(0)

    // Better to lose the click to expiry than to spend it on a routing that is certain to fail.
    expect(bundle.raw.route).not.toHaveBeenCalled()
    expect(log.get('clk_1')?.routedAt).toBeNull()
    loop.stop()
  })

  it('paces itself to the sustained rate instead of sprinting into the throttle', async () => {
    const bundle = stubClient()
    const { loop } = makeLoop(bundle)

    await loop.start()
    await vi.advanceTimersByTimeAsync(0)
    expect(bundle.raw.nextClick).toHaveBeenCalledTimes(1)

    // 20/min means one every 3s. Nothing should be issued a moment before that.
    await vi.advanceTimersByTimeAsync(2_900)
    expect(bundle.raw.nextClick).toHaveBeenCalledTimes(1)

    await vi.advanceTimersByTimeAsync(200)
    expect(bundle.raw.nextClick).toHaveBeenCalledTimes(2)
    loop.stop()
  })

  it('honors retryAfterSeconds when throttled', async () => {
    let attempts = 0
    const bundle = stubClient({
      nextClick: vi.fn(async () => {
        attempts += 1
        if (attempts === 1) throw new ApiError(429, 'RATE_LIMITED', 'slow down', 4)
        return {
          clickId: 'clk_after_throttle',
          merchantId: 42,
          issuedAt: new Date(T0).toISOString(),
          expiresAt: new Date(T0 + 120_000).toISOString(),
        }
      }),
    })
    const { loop } = makeLoop(bundle)

    await loop.start()
    await vi.advanceTimersByTimeAsync(0)

    expect(loop.state.feedError?.code).toBe('RATE_LIMITED')

    // Not before the advertised wait...
    await vi.advanceTimersByTimeAsync(3_900)
    expect(attempts).toBe(1)
    // ...and exactly on it.
    await vi.advanceTimersByTimeAsync(200)
    expect(attempts).toBe(2)
    loop.stop()
  })
})

describe('the commission poll', () => {
  it('stores the cursor after every poll, including empty ones', async () => {
    const bundle = stubClient()
    const { loop } = makeLoop(bundle)

    await loop.start()
    await vi.advanceTimersByTimeAsync(2_000)
    await vi.advanceTimersByTimeAsync(2_000)

    // First poll with no cursor, second with the one the server returned. Discarding it would
    // re-read the feed from the beginning and double-count every commission.
    expect(bundle.feedCalls[0]).toBeNull()
    expect(bundle.feedCalls[1]).toBe('cursor-1')
    loop.stop()
  })

  it('folds a disclosed commission into its click', async () => {
    const bundle = stubClient({
      commissions: vi.fn(async () => ({
        commissions: [
          {
            clickId: 'clk_1',
            merchantId: 42,
            routedNetworkId: 'net-covers',
            routedAt: new Date(T0).toISOString(),
            kind: 'settlement' as const,
            state: 'settled' as const,
            commission: 0.5713,
            errorCode: null,
            disclosedAt: new Date(T0 + 60_000).toISOString(),
          },
        ],
        nextCursor: 'cursor-2',
      })),
    })
    const { loop, log } = makeLoop(bundle)

    await loop.start()
    await vi.advanceTimersByTimeAsync(2_000)

    expect(log.get('clk_1')?.commission).toBe(0.5713)
    expect(log.get('clk_1')?.routedNetworkId).toBe('net-covers')
    expect(clickState(log.get('clk_1')!, T0 + 60_000)).toBe('settled')
    loop.stop()
  })

  it('uses the feed to heal routing fields when every route response is lost', async () => {
    const bundle = stubClient({
      route: vi.fn(async () => {
        throw new TransportError('/route', new Error('response lost'))
      }),
      commissions: vi.fn(async () => ({
        commissions: [
          {
            clickId: 'clk_1',
            merchantId: 42,
            routedNetworkId: 'net-covers',
            routedAt: new Date(T0 + 500).toISOString(),
            kind: 'settlement' as const,
            state: 'settled' as const,
            commission: 0.5713,
            errorCode: null,
            disclosedAt: new Date(T0 + 60_500).toISOString(),
          },
        ],
        nextCursor: 'cursor-2',
      })),
    })
    const { loop, log } = makeLoop(bundle)

    await loop.start()
    await vi.advanceTimersByTimeAsync(2_000)

    expect(log.get('clk_1')).toMatchObject({
      routedAt: T0 + 500,
      routedNetworkId: 'net-covers',
      commission: 0.5713,
    })
    expect(clickState(log.get('clk_1')!, T0 + 60_500)).toBe('settled')
    loop.stop()
  })

  it('counts, rather than dies on, a commission for a click it never issued', async () => {
    // The reload case: the feed is scoped to the credential pair, not to a page load.
    const bundle = stubClient({
      commissions: vi.fn(async () => ({
        commissions: [
          {
            clickId: 'clk_from_a_previous_page_load',
            merchantId: 42,
            routedNetworkId: 'net-covers',
            routedAt: new Date(T0).toISOString(),
            kind: 'settlement' as const,
            state: 'settled' as const,
            commission: 0.31,
            errorCode: null,
            disclosedAt: new Date(T0).toISOString(),
          },
        ],
        nextCursor: 'cursor-2',
      })),
    })
    const { loop } = makeLoop(bundle)

    await loop.start()
    await vi.advanceTimersByTimeAsync(2_000)

    expect(loop.state.skippedDisclosures).toBe(1)
    // And the poll kept going rather than backing off.
    expect(loop.state.feedError).toBeNull()
    loop.stop()
  })

  it('restarts the feed on INVALID_CURSOR instead of retrying a cursor that cannot work', async () => {
    let calls = 0
    const bundle = stubClient({
      commissions: vi.fn(async (cursor: string | null) => {
        calls += 1
        if (calls === 1) return { commissions: [], nextCursor: 'poisoned' }
        if (cursor === 'poisoned') throw new ApiError(400, 'INVALID_CURSOR', 'bad cursor')
        return { commissions: [], nextCursor: 'fresh' }
      }),
    })
    const { loop } = makeLoop(bundle)

    await loop.start()
    await vi.advanceTimersByTimeAsync(2_000)
    await vi.advanceTimersByTimeAsync(2_000)
    expect(loop.state.feedError?.code).toBe('INVALID_CURSOR')

    // Backoff, then a poll from the beginning rather than with the same broken value.
    // Asserted on the mock rather than on bundle.feedCalls, because this test replaces the default
    // implementation that records into it.
    await vi.advanceTimersByTimeAsync(1_000)
    const pollCalls = (bundle.raw.commissions as ReturnType<typeof vi.fn>).mock.calls
    expect(pollCalls[pollCalls.length - 1]?.[0]).toBeNull()
    loop.stop()
  })

  it('keeps an UNAUTHORIZED on screen and lets a RATE_LIMITED be dismissed', async () => {
    const bundle = stubClient({
      commissions: vi.fn(async () => {
        throw new ApiError(401, 'UNAUTHORIZED', 'credentials rotated')
      }),
    })
    const { loop } = makeLoop(bundle)

    await loop.start()
    await vi.advanceTimersByTimeAsync(2_000)

    expect(loop.state.feedError?.code).toBe('UNAUTHORIZED')
    expect(loop.state.feedError?.dismissible).toBe(false)

    // Nothing in the client works until credentials are fixed, so hiding it would only make the
    // resulting silence harder to explain.
    loop.dismissFeedError()
    expect(loop.state.feedError?.code).toBe('UNAUTHORIZED')
    loop.stop()
  })

  it('surfaces a failure that is not the server\'s rather than retrying silently', async () => {
    const bundle = stubClient({
      commissions: vi.fn(async () => {
        throw new TypeError('something broke inside the client')
      }),
    })
    const { loop } = makeLoop(bundle)

    await loop.start()
    await vi.advanceTimersByTimeAsync(2_000)

    expect(loop.state.feedError?.code).toBe('CLIENT_ERROR')
    expect(loop.state.feedError?.message).toMatch(/something broke inside the client/)
    loop.stop()
  })
})

describe('pause and resume', () => {
  it('stops issuing clicks but keeps polling, so in-flight clicks still resolve', async () => {
    const bundle = stubClient()
    const { loop } = makeLoop(bundle)

    await loop.start()
    await vi.advanceTimersByTimeAsync(0)
    const issuedBefore = (bundle.raw.nextClick as ReturnType<typeof vi.fn>).mock.calls.length

    loop.pause()
    await vi.advanceTimersByTimeAsync(10_000)

    expect((bundle.raw.nextClick as ReturnType<typeof vi.fn>).mock.calls.length).toBe(issuedBefore)
    // Polling continued: abandoning it would leave already-routed clicks permanently unresolved.
    expect((bundle.raw.commissions as ReturnType<typeof vi.fn>).mock.calls.length).toBeGreaterThan(1)

    loop.resume()
    await vi.advanceTimersByTimeAsync(0)
    expect((bundle.raw.nextClick as ReturnType<typeof vi.fn>).mock.calls.length).toBeGreaterThan(issuedBefore)
    loop.stop()
  })

  it('issues nothing more after stop', async () => {
    const bundle = stubClient()
    const { loop } = makeLoop(bundle)

    await loop.start()
    await vi.advanceTimersByTimeAsync(0)
    loop.stop()

    const issued = (bundle.raw.nextClick as ReturnType<typeof vi.fn>).mock.calls.length
    const polled = (bundle.raw.commissions as ReturnType<typeof vi.fn>).mock.calls.length
    await vi.advanceTimersByTimeAsync(30_000)

    expect((bundle.raw.nextClick as ReturnType<typeof vi.fn>).mock.calls.length).toBe(issued)
    expect((bundle.raw.commissions as ReturnType<typeof vi.fn>).mock.calls.length).toBe(polled)
  })
})

describe('optimization models', () => {
  it('refuses to be built without a model, or with two models sharing a name', () => {
    const { client } = stubClient()
    const base = { client, log: new ClickLog(), onChange: () => {} }

    expect(() => new RunLoop({ ...base, models: [] })).toThrow(/at least one optimization model/)
    // The name is what a click records about who decided it, so it has to identify one model.
    expect(
      () => new RunLoop({ ...base, models: [randomOptimizationModel(), randomOptimizationModel()] }),
    ).toThrow(/both named "random"/)
  })

  it('shows every model the field before asking any of them to decide', async () => {
    const bundle = stubClient()
    const first = probeModel('first')
    const second = probeModel('second')
    const { loop } = makeLoop(bundle, new ClickLog(), [first.model, second.model])

    await loop.start()

    expect(first.prepared).toEqual([NETWORKS])
    expect(second.prepared).toEqual([NETWORKS])
    expect(first.contexts).toHaveLength(0)
    loop.stop()
  })

  it('prepares nothing and issues nothing when stopped while the reference data was in flight', async () => {
    // React's StrictMode mounts, unmounts and mounts again, so a loop can be stopped before its
    // own start() has finished.
    let release!: (networks: Network[]) => void
    const bundle = stubClient({
      networks: vi.fn(
        () =>
          new Promise<Network[]>((resolve) => {
            release = resolve
          }),
      ),
    })
    const probe = probeModel('probe')
    const { loop } = makeLoop(bundle, new ClickLog(), [probe.model])

    const starting = loop.start()
    loop.stop()
    release(NETWORKS)
    await starting
    await vi.advanceTimersByTimeAsync(10_000)

    expect(probe.prepared).toHaveLength(0)
    expect(loop.state.running).toBe(false)
    expect(bundle.raw.nextClick).not.toHaveBeenCalled()
  })

  it('asks only the active model, and tells it the merchant, the field and the issue time', async () => {
    const bundle = stubClient()
    const active = probeModel('active')
    const idle = probeModel('idle')
    const { loop } = makeLoop(bundle, new ClickLog(), [active.model, idle.model])

    await loop.start()
    await vi.advanceTimersByTimeAsync(0)

    // The click's own issue time on the server's clock, not the browser's: results are stamped by
    // the server too, and a model that ages its knowledge needs both on one clock.
    expect(active.contexts).toEqual([{ merchantId: 42, networks: NETWORKS, now: T0 }])
    expect(idle.contexts).toHaveLength(0)
    loop.stop()
  })

  it('records with each click which model decided it and why', async () => {
    const bundle = stubClient()
    const probe = probeModel('probe')
    const { loop, log } = makeLoop(bundle, new ClickLog(), [probe.model])

    await loop.start()
    await vi.advanceTimersByTimeAsync(0)

    expect(log.get('clk_1')?.decision).toEqual({
      modelName: 'probe',
      rationale: 'probe says so',
      exploratory: true,
    })
    loop.stop()
  })

  it('records null for exploratory when the model has no notion of a favorite', async () => {
    const bundle = stubClient()
    const { loop, log } = makeLoop(bundle)

    await loop.start()
    await vi.advanceTimersByTimeAsync(0)

    expect(log.get('clk_1')?.decision).toEqual({
      modelName: 'random',
      rationale: 'random among 1 networks covering merchant 42',
      exploratory: null,
    })
    loop.stop()
  })

  it('switches which model decides from the next click on, and refuses an unknown name', async () => {
    const bundle = stubClient()
    const first = probeModel('first')
    const second = probeModel('second')
    const { loop, log } = makeLoop(bundle, new ClickLog(), [first.model, second.model])

    await loop.start()
    await vi.advanceTimersByTimeAsync(0)
    expect(loop.state.activeModelName).toBe('first')

    loop.selectModel('second')
    expect(loop.state.activeModelName).toBe('second')
    await vi.advanceTimersByTimeAsync(3_000)

    // The earlier click keeps the model it was decided by: that is a fact about the click.
    expect(log.get('clk_1')?.decision?.modelName).toBe('first')
    expect(log.get('clk_2')?.decision?.modelName).toBe('second')
    expect(() => loop.selectModel('nope')).toThrow(/no optimization model is named "nope"/)
    loop.stop()
  })

  it('tells every model about each settled commission exactly once, even when the feed is re-read', async () => {
    let calls = 0
    const bundle = stubClient({
      commissions: vi.fn(async (cursor: string | null) => {
        calls += 1
        if (calls === 1) return { commissions: [settlement('clk_1', 0.5)], nextCursor: 'poisoned' }
        if (cursor === 'poisoned') throw new ApiError(400, 'INVALID_CURSOR', 'bad cursor')
        // The restarted feed hands the first row over again, along with a new one.
        return { commissions: [settlement('clk_1', 0.5), settlement('clk_2', 0.7)], nextCursor: 'fresh' }
      }),
    })
    const deciding = probeModel('deciding')
    const watching = probeModel('watching')
    const { loop } = makeLoop(bundle, new ClickLog(), [deciding.model, watching.model])

    await loop.start()
    await vi.advanceTimersByTimeAsync(2_000)
    await vi.advanceTimersByTimeAsync(2_000)
    await vi.advanceTimersByTimeAsync(1_000)

    // A model that learned from the same commission twice would be twice as sure as it should be.
    expect(deciding.observations.map((observation) => observation.clickId)).toEqual(['clk_1', 'clk_2'])
    // The model that is not deciding learns too, so it is not stale when it is switched to.
    expect(watching.observations.map((observation) => observation.clickId)).toEqual(['clk_1', 'clk_2'])
    expect(deciding.observations[0]).toEqual({
      clickId: 'clk_1',
      networkId: 'net-covers',
      commission: 0.5,
      routedAt: T0,
      disclosedAt: T0 + 60_000,
    })
    loop.stop()
  })

  it('passes on commissions for clicks this page never issued, and never a rejection', async () => {
    // The reload case. What a network paid is worth knowing whoever routed the click, and replaying
    // the feed is how a model gets its knowledge back after a reload. A rejection carries no
    // commission: it says the routing was a mistake, not what the network pays.
    const bundle = stubClient({
      commissions: vi.fn(async () => ({
        commissions: [settlement('clk_from_a_previous_page_load', 0.31), rejection('clk_rejected_before')],
        nextCursor: 'cursor-2',
      })),
    })
    const probe = probeModel('probe')
    const { loop } = makeLoop(bundle, new ClickLog(), [probe.model])

    await loop.start()
    await vi.advanceTimersByTimeAsync(2_000)
    await vi.advanceTimersByTimeAsync(2_000)

    expect(probe.observations.map((observation) => observation.clickId)).toEqual([
      'clk_from_a_previous_page_load',
    ])
    // Two rows the log has no click for, each counted once however often the feed repeats them.
    expect(loop.state.skippedDisclosures).toBe(2)
    loop.stop()
  })

  it('surfaces a model that throws while learning, without starving the others or the feed', async () => {
    const quiet = vi.spyOn(console, 'error').mockImplementation(() => {})
    const commissions = vi.fn(async (_cursor: string | null) => ({
      commissions: [settlement('clk_1', 0.5)],
      nextCursor: 'cursor-2',
    }))
    const bundle = stubClient({ commissions })
    const broken: OptimizationModel = {
      name: 'broken',
      choose: () => ({ networkId: 'net-covers', rationale: 'always the same' }),
      observe: () => {
        throw new Error('the estimate became NaN')
      },
    }
    const healthy = probeModel('healthy')
    const { loop } = makeLoop(bundle, new ClickLog(), [broken, healthy.model])

    await loop.start()
    await vi.advanceTimersByTimeAsync(2_000)

    expect(loop.state.feedError?.code).toBe('MODEL_ERROR')
    expect(loop.state.feedError?.message).toMatch(/"broken".*the estimate became NaN/)
    expect(healthy.observations).toHaveLength(1)

    // The cursor still advanced. Had the exception escaped the poll, the same row would be read
    // into the same exception on every turn.
    await vi.advanceTimersByTimeAsync(2_000)
    expect(commissions.mock.calls.map(([cursor]) => cursor)).toEqual([null, 'cursor-2'])
    // And a successful poll does not quietly clear it: nothing the server did fixed the model.
    expect(loop.state.feedError?.code).toBe('MODEL_ERROR')

    loop.dismissFeedError()
    expect(loop.state.feedError).toBeNull()
    loop.stop()
    quiet.mockRestore()
  })

  it('surfaces a model that throws while choosing, leaves the click un-routed, and keeps issuing', async () => {
    const quiet = vi.spyOn(console, 'error').mockImplementation(() => {})
    const bundle = stubClient()
    const broken: OptimizationModel = {
      name: 'broken',
      choose: () => {
        throw new Error('no belief for this network')
      },
    }
    const { loop, log } = makeLoop(bundle, new ClickLog(), [broken])

    await loop.start()
    await vi.advanceTimersByTimeAsync(0)

    expect(loop.state.feedError?.code).toBe('MODEL_ERROR')
    // Not routed on the model's behalf: the loop does not guess where a click should go.
    expect(bundle.raw.route).not.toHaveBeenCalled()
    expect(log.get('clk_1')).toMatchObject({ routedAt: null, decision: null })

    // The click loop keeps its cadence instead of dying with the header still saying "running".
    await vi.advanceTimersByTimeAsync(3_000)
    expect(bundle.raw.nextClick).toHaveBeenCalledTimes(2)
    loop.stop()
    quiet.mockRestore()
  })
})
