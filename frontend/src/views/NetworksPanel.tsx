import type { NetworkInsight, NetworkInsightResponse } from '../api/types'
import { ago, count, rate, signedPercent, signedRate } from '../lib/format'
import { InlineError } from './Notice'
import { CELL, MUTED, WARN_TAG } from './style'

/**
 * Decision insight per network: what each one CLAIMS against what it PAID, on how much evidence,
 * how recently, and whether that evidence is thin, stale or changing.
 *
 * The three flags are the operator's guard against reading one lucky observation as a trend, and the
 * legend spells out the exact rule behind each so nobody has to guess what "thin" means.
 */
function Flags({ network }: { network: NetworkInsight }) {
  return (
    <span style={{ display: 'inline-flex', gap: 4, flexWrap: 'wrap' }}>
      {network.thin ? (
        <span className="tag tag-neutral" title="Fewer than 10 settled clicks">
          Thin
        </span>
      ) : null}
      {network.stale ? (
        <span className="tag tag-outline" title="No disclosure in the last 5 minutes">
          Stale
        </span>
      ) : null}
      {network.changing ? (
        <span className="tag" style={WARN_TAG} title="Recent mean differs from the all-time mean beyond 1.96 standard errors">
          Changing
        </span>
      ) : null}
      {!network.thin && !network.stale && !network.changing ? <span style={{ color: MUTED(35) }}>—</span> : null}
    </span>
  )
}

function Trend({ trend }: { trend: number | null }) {
  if (trend === null) return <span style={{ color: MUTED(35) }}>—</span>
  const arrow = trend > 0 ? '▲' : trend < 0 ? '▼' : '='
  return (
    <span className="tnum" style={{ color: trend === 0 ? MUTED(55) : 'var(--color-text)' }}>
      {arrow} {signedRate(trend)}
    </span>
  )
}

export function NetworksPanel({ insight, error }: { insight: NetworkInsightResponse | null; error: Error | null }) {
  const muted = { color: MUTED(35) }
  const right = { ...CELL, textAlign: 'right' as const }

  return (
    <>
      <div style={{ marginTop: 26, marginBottom: 20 }}>
        <h3 style={{ margin: '0 0 6px' }}>Networks</h3>
        <div style={{ fontSize: 13, color: MUTED(55) }}>
          {insight === null
            ? 'Reading network insight…'
            : insight.runId === null
              ? 'No run yet, so no catalogue to describe.'
              : `Advertised against realized for run #${insight.runId}'s catalogue. Realized is the mean commission over settled clicks with its 95% interval; recent is the last ${insight.recentWindowMinutes} minutes.`}
        </div>
      </div>

      <InlineError error={error} prefix="Network insight could not refresh" />

      <table className="table">
        <thead>
          <tr>
            <th>Network</th>
            <th style={{ textAlign: 'right' }}>Advertised</th>
            <th style={{ textAlign: 'right' }}>Realized (95% CI)</th>
            <th style={{ textAlign: 'right' }}>Claim gap</th>
            <th style={{ textAlign: 'right' }}>n</th>
            <th style={{ textAlign: 'right' }}>Recent mean</th>
            <th style={{ textAlign: 'right' }}>Trend</th>
            <th>Last disclosure</th>
            <th style={{ textAlign: 'right' }}>Coverage</th>
            <th style={{ textAlign: 'right' }}>Pending</th>
            <th style={{ textAlign: 'right' }}>Rejected</th>
            <th>Evidence</th>
          </tr>
        </thead>
        <tbody>
          {(insight?.networks ?? []).map((network) => (
            <tr key={network.networkId} data-network-id={network.networkId}>
              <td style={CELL}>
                {network.name}
                <span style={{ fontSize: 11, color: MUTED(40) }}> · {network.networkId}</span>
              </td>
              <td className="tnum" style={right}>
                {rate(network.advertisedRate)}
              </td>
              <td className="tnum" style={right}>
                {network.mean === null ? (
                  <span style={muted}>no settled clicks</span>
                ) : (
                  <>
                    {rate(network.mean)}
                    {network.ciLow !== null && network.ciHigh !== null ? (
                      <div style={{ fontSize: 11, color: MUTED(45) }}>
                        {rate(network.ciLow)} – {rate(network.ciHigh)}
                      </div>
                    ) : (
                      <div style={{ fontSize: 11, color: MUTED(45) }}>interval needs 2+ samples</div>
                    )}
                  </>
                )}
              </td>
              <td className="tnum" style={right}>
                {network.claimGap === null ? (
                  <span style={muted}>—</span>
                ) : (
                  <>
                    {signedRate(network.claimGap)}
                    {network.claimGapPercent !== null ? (
                      <div style={{ fontSize: 11, color: MUTED(45) }}>{signedPercent(network.claimGapPercent)}</div>
                    ) : null}
                  </>
                )}
              </td>
              <td className="tnum" style={right}>
                {count(network.settled)}
              </td>
              <td className="tnum" style={right}>
                {network.recentMean === null ? (
                  <span style={muted}>—</span>
                ) : (
                  <>
                    {rate(network.recentMean)}
                    <div style={{ fontSize: 11, color: MUTED(45) }}>n = {count(network.recentSettled)}</div>
                  </>
                )}
              </td>
              <td style={right}>
                <Trend trend={network.trend} />
              </td>
              <td style={CELL} className="tnum">
                {insight !== null ? ago(network.lastDisclosedAt, insight.serverNow) : '—'}
              </td>
              <td className="tnum" style={right}>
                {count(network.coverageCount)}
              </td>
              <td className="tnum" style={right}>
                {count(network.pending)}
              </td>
              <td className="tnum" style={right}>
                {count(network.rejected)}
              </td>
              <td style={CELL}>
                <Flags network={network} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      {insight !== null && insight.networks.length === 0 ? (
        <div style={{ padding: '18px 0', fontSize: 14, color: MUTED(45) }}>No networks in scope.</div>
      ) : null}

      <dl
        style={{
          display: 'grid',
          gridTemplateColumns: 'max-content 1fr',
          columnGap: 12,
          rowGap: 4,
          margin: '18px 0 0',
          fontSize: 12,
          color: MUTED(55),
        }}
      >
        <dt>
          <span className="tag tag-neutral">Thin</span>
        </dt>
        <dd style={{ margin: 0 }}>fewer than 10 settled clicks: the mean is mostly noise.</dd>
        <dt>
          <span className="tag tag-outline">Stale</span>
        </dt>
        <dd style={{ margin: 0 }}>no disclosure in the last 5 minutes: the figures may describe a world that has moved on.</dd>
        <dt>
          <span className="tag" style={WARN_TAG}>
            Changing
          </span>
        </dt>
        <dd style={{ margin: 0 }}>
          the recent mean differs from the all-time mean by more than 1.96 standard errors: the network&apos;s
          behaviour has shifted, and the all-time figure mixes two regimes.
        </dd>
        <dt style={{ color: MUTED(70) }}>Claim gap</dt>
        <dd style={{ margin: 0 }}>realized mean minus advertised rate. Negative means the network pays less than it claims.</dd>
        <dt style={{ color: MUTED(70) }}>Coverage</dt>
        <dd style={{ margin: 0 }}>how many merchants the network accepts: the constraint on where a model may send a click.</dd>
      </dl>
    </>
  )
}
