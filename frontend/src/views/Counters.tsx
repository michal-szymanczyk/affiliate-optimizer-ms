import { useCallback } from 'react'
import { operatorApi } from '../api/operator'
import { POLL_INTERVAL_MS, usePolling } from '../hooks/usePolling'
import { count, duration, elapsedMs, money, rate } from '../lib/format'
import { InlineError } from './Notice'
import { KICKER, MUTED } from './style'

/**
 * The four run counters pinned above the feed.
 *
 * Each is a blueprint card with corner registration marks — Industry draws cards as hairline line
 * drawings with no surface fill, so the marks are the card, not decoration.
 *
 * The numbers come from `GET /api/clicks/getcounters`, derived on the host at server time. The tile
 * that used to tick on the browser clock ("running for") is now `serverNow - startedAt`, refreshed
 * every poll: a laptop with a wrong clock shows the right runtime.
 */
function Counter({
  id,
  kicker,
  value,
  sub,
  note,
}: {
  /** Stable machine key, deliberately NOT the kicker: the kicker is display copy, and Industry
   *  uppercases it in CSS, so keying tests off it would couple them to wording and to styling. */
  id: string
  kicker: string
  value: string
  sub: string
  /** An honest footnote, shown only when there is something to say. */
  note?: string
}) {
  return (
    /*
     * `data-counter` is a test hook. The tiles are three anonymous divs by design — a kicker, a big
     * number, a sub-line — with nothing semantic to query by, so an e2e test would otherwise have to
     * assert on nth-child positions and break the moment the layout moved.
     */
    <div
      className="card blueprint"
      data-counter={id}
      style={{ padding: '16px 18px', display: 'grid', gap: 6, alignContent: 'start' }}
    >
      <i className="corner tl" />
      <i className="corner tr" />
      <i className="corner bl" />
      <i className="corner br" />
      <div style={KICKER}>{kicker}</div>
      <div
        data-counter-value=""
        style={{
          fontFamily: 'var(--font-heading)',
          fontWeight: 600,
          fontSize: 34,
          lineHeight: 1,
          fontVariantNumeric: 'tabular-nums',
        }}
      >
        {value}
      </div>
      <div style={{ fontSize: 11, color: MUTED(50) }}>{sub}</div>
      {note !== undefined ? (
        <div style={{ fontSize: 11, color: 'var(--color-accent-700)' }} data-counter-note="">
          {note}
        </div>
      ) : null}
    </div>
  )
}

export function Counters({
  runId,
  endedAt,
}: {
  /** The run in scope; undefined when no run exists yet. */
  runId: number | undefined
  /** When the run in scope has stopped, its end: "ran for" rather than a runtime that keeps growing. */
  endedAt: string | null
}) {
  const fetcher = useCallback(() => operatorApi.getCounters(runId), [runId])
  const { data, error } = usePolling(fetcher, POLL_INTERVAL_MS)

  const runtime =
    data === null || data.startedAt === null ? '—' : duration(elapsedMs(data.startedAt, endedAt ?? data.serverNow))

  return (
    <>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 14, marginBottom: 14 }}>
        <Counter
          id="running-for"
          kicker={endedAt === null ? 'Running for' : 'Ran for'}
          value={runtime}
          sub={endedAt === null ? 'since the run started, by the server clock' : 'the run has stopped'}
        />
        <Counter
          id="clicks-processed"
          kicker="Clicks processed"
          value={data === null ? '—' : count(data.processed)}
          sub={
            data === null
              ? ''
              : `${count(data.completed)} completed · ${count(data.awaitingDisclosure)} awaiting disclosure`
          }
        />
        <Counter
          id="clicks-completed"
          kicker="Clicks completed"
          value={data === null ? '—' : count(data.completed)}
          sub={
            data === null
              ? ''
              : `${count(data.settled)} settled · ${count(data.rejected)} rejected · ${count(data.expired)} expired`
          }
        />
        <Counter
          id="commission"
          kicker="Commission"
          value={data === null ? '—' : money(data.totalCommission)}
          sub={
            data === null
              ? ''
              : data.perSettledClick === null
                ? 'no settled clicks yet'
                : `${rate(data.perSettledClick)} per settled click`
          }
          {...(data !== null && data.orphanDisclosures > 0
            ? { note: `${count(data.orphanDisclosures)} disclosures for clicks this store never issued` }
            : {})}
        />
      </div>
      <InlineError error={error} prefix="Counters could not refresh" />
    </>
  )
}
