import { useCallback } from 'react'
import { operatorApi } from '../api/operator'
import type { Arm, CompareResponse, Verdict } from '../api/types'
import { POLL_INTERVAL_MS, usePolling } from '../hooks/usePolling'
import { count, money, rate, signedRate, utcDateTime } from '../lib/format'
import { AlertIcon } from './AlertIcon'
import { InlineError } from './Notice'
import { CELL, MUTED, WARN_TAG } from './style'

/**
 * Model comparison for the run in scope: one arm per model, judged on the clicks it decided.
 *
 * The verdict comes from the host and is deliberately cautious: inconclusive below the sample floor,
 * "no separation" when the intervals overlap, and a caveat whenever an arm's first and second halves
 * disagree. This panel never shows a bare winner -- the caveats are rendered beside the badge, at the
 * same weight, because they are the finding.
 */
const VERDICT: Record<Verdict, { label: string; className: string }> = {
  inconclusive: { label: 'Inconclusive', className: 'tag tag-neutral' },
  noSeparation: { label: 'No separation', className: 'tag tag-outline' },
  leading: { label: 'Leading', className: 'tag tag-accent' },
}

function Progress({ settled, target }: { settled: number; target: number }) {
  const percent = target === 0 ? 100 : Math.min(100, Math.round((settled / target) * 100))
  return (
    <div style={{ display: 'grid', gap: 3, minWidth: 110 }}>
      <span className="tnum">
        {count(settled)} / {count(target)}
      </span>
      <div style={{ height: 3, background: MUTED(12) }} aria-hidden="true">
        <div style={{ width: `${percent}%`, height: '100%', background: 'var(--color-accent)' }} />
      </div>
    </div>
  )
}

function Halves({ arm }: { arm: Arm }) {
  if (arm.firstHalfMean === null && arm.secondHalfMean === null) return <span style={{ color: MUTED(35) }}>—</span>
  const show = (value: number | null) => (value === null ? '—' : rate(value))
  return (
    <span className="tnum">
      {show(arm.firstHalfMean)} → {show(arm.secondHalfMean)}
    </span>
  )
}

export function ComparePanel({ runId }: { runId: number | undefined }) {
  const fetcher = useCallback(() => operatorApi.compareModels(runId), [runId])
  const { data, error } = usePolling(fetcher, POLL_INTERVAL_MS)
  const right = { ...CELL, textAlign: 'right' as const }

  return (
    <>
      <div style={{ marginTop: 26, marginBottom: 20 }}>
        <h3 style={{ margin: '0 0 6px' }}>Compare</h3>
        <div style={{ fontSize: 13, color: MUTED(55) }}>
          {data === null ? 'Reading the comparison…' : <Design data={data} />}
        </div>
      </div>

      <InlineError error={error} prefix="The comparison could not refresh" />

      {data !== null ? (
        <>
          <div
            className="blueprint"
            style={{ padding: '14px 18px', marginBottom: 20, display: 'grid', gap: 10 }}
            data-verdict={data.verdict}
          >
            <i className="corner tl" />
            <i className="corner tr" />
            <i className="corner bl" />
            <i className="corner br" />
            <div style={{ display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
              <span className={VERDICT[data.verdict].className} style={{ fontWeight: 500 }}>
                {VERDICT[data.verdict].label}
              </span>
              <span style={{ fontSize: 14 }}>
                {data.verdict === 'leading' && data.leadingModel !== null
                  ? `${data.leadingModel} leads on this evidence — subject to every caveat below.`
                  : data.verdict === 'noSeparation'
                    ? 'No arm is separated from the others on this evidence.'
                    : data.leadingModel !== null
                      ? `${data.leadingModel} is ahead on the whole run, but the evidence does not support calling it.`
                      : 'The evidence does not support a conclusion yet.'}
              </span>
            </div>
            <ul style={{ margin: 0, paddingLeft: 0, listStyle: 'none', display: 'grid', gap: 4, fontSize: 13 }}>
              {data.caveats.map((caveat, index) => (
                <li key={index} style={{ display: 'flex', gap: 8, alignItems: 'flex-start', color: MUTED(80) }}>
                  <AlertIcon size={12} color="var(--color-accent-700)" />
                  <span>{caveat}</span>
                </li>
              ))}
            </ul>
          </div>

          <table className="table">
            <thead>
              <tr>
                <th>Model (arm)</th>
                <th style={{ textAlign: 'right' }}>Decided</th>
                <th style={{ textAlign: 'right' }}>Routed</th>
                <th>Settled / floor</th>
                <th style={{ textAlign: 'right' }}>Pending</th>
                <th style={{ textAlign: 'right' }}>Rejected</th>
                <th style={{ textAlign: 'right' }}>Expired</th>
                <th style={{ textAlign: 'right' }}>Commission</th>
                <th style={{ textAlign: 'right' }}>Mean (95% CI)</th>
                <th style={{ textAlign: 'right' }}>Advertised of chosen</th>
                <th style={{ textAlign: 'right' }}>Claim gap</th>
                <th>First half → second half</th>
                <th>Conditions</th>
              </tr>
            </thead>
            <tbody>
              {data.arms.map((arm) => (
                <tr key={arm.modelName} data-arm={arm.modelName}>
                  <td style={{ ...CELL, fontWeight: 500 }}>{arm.modelName}</td>
                  <td className="tnum" style={right}>
                    {count(arm.decided)}
                  </td>
                  <td className="tnum" style={right}>
                    {count(arm.routed)}
                  </td>
                  <td style={CELL}>
                    <Progress settled={arm.settled} target={data.minSamplesPerArm} />
                  </td>
                  <td className="tnum" style={right}>
                    {count(arm.pending)}
                  </td>
                  <td className="tnum" style={right}>
                    {count(arm.rejected)}
                  </td>
                  <td className="tnum" style={right}>
                    {count(arm.expired)}
                  </td>
                  <td className="tnum" style={right}>
                    {money(arm.totalCommission)}
                  </td>
                  <td className="tnum" style={right}>
                    {arm.mean === null ? (
                      <span style={{ color: MUTED(35) }}>no settled clicks</span>
                    ) : (
                      <>
                        {rate(arm.mean)}
                        {arm.ciLow !== null && arm.ciHigh !== null ? (
                          <div style={{ fontSize: 11, color: MUTED(45) }}>
                            {rate(arm.ciLow)} – {rate(arm.ciHigh)}
                          </div>
                        ) : null}
                      </>
                    )}
                  </td>
                  <td className="tnum" style={right}>
                    {arm.meanAdvertisedOfChosen === null ? <span style={{ color: MUTED(35) }}>—</span> : rate(arm.meanAdvertisedOfChosen)}
                  </td>
                  <td className="tnum" style={right}>
                    {arm.claimGap === null ? <span style={{ color: MUTED(35) }}>—</span> : signedRate(arm.claimGap)}
                  </td>
                  <td style={CELL}>
                    <Halves arm={arm} />
                  </td>
                  <td style={CELL}>
                    {arm.conditionsChanged ? (
                      <span className="tag" style={{ ...WARN_TAG, gap: 5 }}>
                        <AlertIcon size={11} /> conditions changed
                      </span>
                    ) : (
                      <span style={{ color: MUTED(45), fontSize: 12 }}>steady</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          {data.arms.length === 0 ? (
            <div style={{ padding: '18px 0', fontSize: 14, color: MUTED(45) }}>No arms to compare.</div>
          ) : (
            <div style={{ fontSize: 12, color: MUTED(45), marginTop: 14 }}>
              Unit of comparison: mean realized commission per settled click of an arm&apos;s own decisions, inside
              one run. A run needs {count(data.minSamplesPerArm)} settled clicks per arm before the verdict can be
              anything but inconclusive.
            </div>
          )}
        </>
      ) : null}
    </>
  )
}

/** How the arms shared traffic, which is what makes them comparable -- or not. */
function Design({ data }: { data: CompareResponse }) {
  if (data.runId === null) return <>No run to compare.</>
  const when =
    data.startedAt === null
      ? ''
      : ` · started ${utcDateTime(data.startedAt)} UTC${data.stoppedAt !== null ? `, stopped ${utcDateTime(data.stoppedAt)} UTC` : ', still running'}`
  return (
    <>
      Run #{data.runId} ·{' '}
      {data.arms.length < 2
        ? 'a single model, so there is no counterfactual and nothing to compare against'
        : data.interleaved
          ? 'interleaved: every arm saw the same clock window and, on average, the same merchant mix'
          : 'not interleaved: arms did not share traffic, so differences may be the world moving rather than the model'}
      {when}
    </>
  )
}
