import { count, duration, money, rate } from '../lib/format'
import type { RunCounters } from '../loop/clickLog'

/**
 * The four run counters pinned above the feed.
 *
 * Each is a blueprint card with corner registration marks — Industry draws cards as hairline line
 * drawings with no surface fill, so the marks are the card, not decoration.
 */
function Counter({
  id,
  kicker,
  value,
  sub,
}: {
  /** Stable machine key, deliberately NOT the kicker: the kicker is display copy, and Industry
   *  uppercases it in CSS, so keying tests off it would couple them to wording and to styling. */
  id: string
  kicker: string
  value: string
  sub: string
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
      style={{ padding: '16px 18px', display: 'grid', gap: 6 }}
    >
      <i className="corner tl" />
      <i className="corner tr" />
      <i className="corner bl" />
      <i className="corner br" />
      <div
        style={{
          fontSize: 10,
          letterSpacing: '.1em',
          textTransform: 'uppercase',
          color: 'var(--color-accent)',
        }}
      >
        {kicker}
      </div>
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
      <div style={{ fontSize: 11, color: 'color-mix(in srgb, var(--color-text) 50%, transparent)' }}>
        {sub}
      </div>
    </div>
  )
}

export function Counters({
  counters,
  startedAt,
  now,
}: {
  counters: RunCounters
  startedAt: number | null
  now: number
}) {
  return (
    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 14, marginBottom: 14 }}>
      <Counter
        id="running-for"
        kicker="Running for"
        value={startedAt === null ? '—' : duration(now - startedAt)}
        sub="since the first authenticated call"
      />
      <Counter
        id="clicks-processed"
        kicker="Clicks processed"
        value={count(counters.processed)}
        sub={`${count(counters.resolved)} resolved · ${count(counters.awaitingDisclosure)} awaiting disclosure`}
      />
      <Counter
        id="clicks-completed"
        kicker="Clicks completed"
        value={count(counters.completed)}
        sub={`${count(counters.settled)} settled · ${count(counters.rejected)} rejected · ${count(counters.expired)} expired`}
      />
      <Counter
        id="commission"
        kicker="Commission"
        value={money(counters.totalCommission)}
        sub={
          counters.perSettledClick === null
            ? 'no settled clicks yet'
            : `${rate(counters.perSettledClick)} per settled click`
        }
      />
    </div>
  )
}
