import { useId, useState } from 'react'
import { count, duration, rate } from '../lib/format'
import type { DecisionSummary } from '../loop/clickLog'
import type { NetworkBeliefView } from '../models/thompson'
import type { Network } from '../api/client'
import { ThompsonDistributionChart } from './ThompsonDistributionChart'

const MUTED = (percent: number) => `color-mix(in srgb, var(--color-text) ${percent}%, transparent)`

const NUMERIC = { textAlign: 'right', fontVariantNumeric: 'tabular-nums' } as const

function percent(share: number): string {
  return `${Math.round(share * 100)}%`
}

/**
 * What the Thompson model currently believes about every network.
 *
 * This is the model's whole state, shown as it is, so an operator can check any decision against
 * it. Three columns say how far to trust a row.
 *
 *   "Usually" is what the network has paid across all of its results, and how much that has varied.
 *   A wide spread there marks a network that swings, which is why the model keeps going back to it.
 *
 *   "Estimate" and "±" are what the model thinks the network pays right now and how unsure it is.
 *   Both move while a network goes unseen: the estimate fades toward "Usually" and the doubt grows
 *   toward its spread.
 *
 *   "Newest result" is how old the evidence is. It never reads less than the disclosure delay.
 *
 * A network with no results yet reads "(prior)": its row is the advertised claim and nothing more,
 * and the panel should not dress that up as an estimate.
 *
 * "Show distributions" draws the same beliefs as the curves the model samples from. Those use the
 * doubt left after counting results still on their way, which is narrower than the table's "±".
 *
 * The table deliberately does not use Industry's `.table` class. See `.belief-table` in app.css.
 */
export function BeliefsPanel({
  networks,
  beliefs,
  summary,
  deciding,
  now,
}: {
  networks: readonly Network[]
  beliefs: readonly NetworkBeliefView[]
  summary: DecisionSummary
  /** Whether this model is the one deciding clicks right now. It learns either way. */
  deciding: boolean
  now: number
}) {
  const [showDistributions, setShowDistributions] = useState(false)
  // The chart's settings live here: the chart is unmounted while closed and would forget them.
  const [hiddenCurves, setHiddenCurves] = useState<ReadonlySet<string>>(() => new Set())
  const [sharedDensity, setSharedDensity] = useState(false)
  const chartId = useId()
  const byId = new Map(networks.map((network) => [network.id, network]))
  const rows = beliefs
    .flatMap((belief) => {
      const network = byId.get(belief.networkId)
      return network ? [{ belief, network }] : []
    })
    .sort(
      (a, b) => b.belief.estimate - a.belief.estimate || a.network.name.localeCompare(b.network.name),
    )

  function toggleCurve(networkId: string) {
    setHiddenCurves((current) => {
      const next = new Set(current)
      if (next.has(networkId)) next.delete(networkId)
      else next.add(networkId)
      return next
    })
  }

  return (
    <section aria-labelledby="beliefs-heading" style={{ marginTop: 26 }}>
      <div className="belief-panel-heading">
        <h3 id="beliefs-heading" style={{ margin: 0 }}>Network beliefs</h3>
        <button type="button" className="btn btn-secondary" aria-expanded={showDistributions}
          aria-controls={chartId} onClick={() => setShowDistributions((shown) => !shown)}>
          {showDistributions ? 'Hide distributions' : 'Show distributions'}
        </button>
      </div>
      <div style={{ fontSize: 13, color: MUTED(55), marginBottom: 14 }}>
        What the thompson model thinks each network pays per click. It learns from every disclosed
        result, whichever model decided the click
        {deciding ? ', and it is deciding now.' : ', but another model is deciding now.'}
        {summary.decided > 0
          ? ` Exploring ${percent(summary.exploratory / summary.decided)} of its last ${count(summary.decided)} decisions.`
          : null}
      </div>

      <div id={chartId}>
        {showDistributions ? (
          <ThompsonDistributionChart
            rows={rows}
            hidden={hiddenCurves}
            onToggle={toggleCurve}
            sharedDensity={sharedDensity}
            onSharedDensity={setSharedDensity}
          />
        ) : null}
      </div>

      {rows.length === 0 ? (
        <div style={{ padding: '6px 0 18px', fontSize: 14, color: MUTED(45) }}>
          No beliefs yet. Each network starts from its advertised rate once the network list has loaded.
        </div>
      ) : (
        <div className="belief-table-scroll">
          <table className="belief-table">
            <thead>
              <tr>
                <th style={{ width: '22%' }}>Network</th>
                <th style={{ width: '11%', textAlign: 'right' }}>Advertised</th>
                <th
                  style={{ width: '19%', textAlign: 'right' }}
                  title="What it has paid across all of its results, and how much that has varied"
                >
                  Usually
                </th>
                <th style={{ width: '11%', textAlign: 'right' }} title="What the model thinks it pays right now">
                  Estimate
                </th>
                <th style={{ width: '9%', textAlign: 'right' }} title="One standard deviation of doubt, right now">
                  ±
                </th>
                <th style={{ width: '12%', textAlign: 'right' }}>Newest result</th>
                <th style={{ width: '7%', textAlign: 'right' }}>Results</th>
                <th style={{ width: '9%', textAlign: 'right' }}>Last {count(summary.routed)} clicks</th>
              </tr>
            </thead>
            <tbody>
              {rows.map(({ belief, network }) => {
                const share = summary.shareByNetwork.get(network.id)
                const unseen = belief.observations === 0
                return (
                  <tr key={network.id}>
                    <td>{network.name}</td>
                    <td style={{ ...NUMERIC, color: MUTED(60) }}>{rate(network.advertisedRate)}</td>
                    <td style={{ ...NUMERIC, color: MUTED(60) }}>
                      {unseen ? '—' : `${rate(belief.usual)} ± ${rate(belief.usualSpread)}`}
                    </td>
                    <td style={NUMERIC}>
                      {unseen ? <span style={{ color: MUTED(45) }}>(prior)</span> : rate(belief.estimate)}
                    </td>
                    <td style={{ ...NUMERIC, color: MUTED(60) }}>{rate(belief.uncertainty)}</td>
                    <td style={{ ...NUMERIC, color: MUTED(60) }}>
                      {belief.newestResultAt === null ? 'never' : `${duration(now - belief.newestResultAt)} ago`}
                    </td>
                    <td style={{ ...NUMERIC, color: MUTED(60) }}>{count(belief.observations)}</td>
                    <td style={NUMERIC}>{share === undefined ? '—' : percent(share)}</td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}
