import { useEffect, useId, useLayoutEffect, useRef, useState } from 'react'
import type { Network } from '../api/client'
import type { NetworkBeliefView } from '../models/thompson'
import { count, rate } from '../lib/format'
import {
  distributionBounds,
  distributionPoints,
  formatCpcTick,
  isPointDistribution,
  isValidDistribution,
  niceTicks,
  normalDensity,
  seriesStyles,
  stabilizeBounds,
} from '../lib/thompsonDistribution'
import type { DistributionBounds } from '../lib/thompsonDistribution'

export interface DistributionRow {
  network: Network
  belief: NetworkBeliefView
}

const HEIGHT = 300
const TOP = 30
const BOTTOM = HEIGHT - 54
const LEFT = 62
const RIGHT = 20

function densityLabel(value: number): string {
  if (value === 0) return '0'
  return value >= 10_000 || value < 0.001
    ? value.toExponential(1)
    : Number(value.toPrecision(3)).toString()
}

function plural(n: number, noun: string): string {
  return `${count(n)} ${noun}${n === 1 ? '' : 's'}`
}

/** A tenth of a pixel is finer than the screen shows, and keeps the path strings short. */
function px(value: number): number {
  return Math.round(value * 10) / 10
}

function describeDistribution({ network, belief }: DistributionRow): string {
  return `${network.name}${belief.observations === 0 ? ' (prior)' : ''}: ` +
    `mean ${rate(belief.estimate)}, sampling σ ${rate(belief.samplingUncertainty)}, ` +
    `belief ± ${rate(belief.uncertainty)}, ${plural(belief.inFlight, 'pending result')}`
}

/**
 * The distributions the Thompson model draws from, one curve per network.
 *
 * Every curve is drawn on one density scale, so the heights compare: a network the model is sure
 * of stands many times taller than one it doubts.
 *
 * Which curves are hidden belongs to the panel, so it outlives this chart being closed and opened
 * again.
 */
export function ThompsonDistributionChart({
  rows,
  hidden,
  onToggle,
}: {
  rows: readonly DistributionRow[]
  /** Ids of the networks whose curves are switched off. */
  hidden: ReadonlySet<string>
  onToggle: (networkId: string) => void
}) {
  const id = useId()
  const container = useRef<HTMLDivElement>(null)
  const [width, setWidth] = useState(960)
  // Kept apart: leaving with the mouse must not drop the highlight of the button that has focus.
  const [hovered, setHovered] = useState<string | null>(null)
  const [focused, setFocused] = useState<string | null>(null)
  const series = rows
    .map((row) => ({ ...row, mean: row.belief.estimate, sd: row.belief.samplingUncertainty }))
    .sort((a, b) => a.network.name.localeCompare(b.network.name) || a.network.id.localeCompare(b.network.id))
  // From every network, hidden or not, so switching one off repaints none of the others.
  const styles = seriesStyles(series.map((row) => row.network.id))
  const valid = series.filter(isValidDistribution)
  const visible = valid.filter((row) => !hidden.has(row.network.id))
  const highlighted = hovered ?? focused
  const activeHighlight = visible.some((row) => row.network.id === highlighted) ? highlighted : null
  const desired = distributionBounds(visible)
  const selection = JSON.stringify(visible.map((row) => row.network.id))
  // The axes last drawn, to steady the next ones against. A ref rather than state: it is only ever
  // read by the following render, and state would cost a second render for every change of scale.
  const drawn = useRef<{ selection: string; bounds: DistributionBounds } | null>(null)
  const last = drawn.current
  const bounds = desired && last && last.selection === selection ? stabilizeBounds(last.bounds, desired) : desired

  useEffect(() => {
    drawn.current = bounds ? { selection, bounds } : null
  })

  // Before paint, so the first frame is already drawn at the width it has.
  useLayoutEffect(() => {
    const element = container.current
    if (!element) return
    const resize = (measured: number) => {
      if (measured > 0) setWidth(Math.max(220, Math.round(measured)))
    }
    resize(element.getBoundingClientRect().width)
    if (typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver(([entry]) => {
      if (entry) resize(entry.contentRect.width)
    })
    observer.observe(element)
    return () => observer.disconnect()
  }, [])

  const plotWidth = width - LEFT - RIGHT
  const xTicks = bounds ? niceTicks(bounds.xMin, bounds.xMax, Math.max(2, Math.floor(plotWidth / 110))) : null
  const yTop = bounds?.yMax ?? 1
  const yTicks = niceTicks(0, yTop, 4).values
  const xAt = (value: number) => px(LEFT +
    (value - (bounds?.xMin ?? 0)) / ((bounds?.xMax ?? 1) - (bounds?.xMin ?? 0)) * plotWidth)
  const yAt = (value: number) => px(BOTTOM - value / yTop * (BOTTOM - TOP))
  const pointMass = visible.some(isPointDistribution)
  const unavailable = series.length - valid.length

  return (
    <figure className="belief-distributions" aria-labelledby={`${id}-heading`}>
      <figcaption>
        <div className="belief-chart-header">
          <h4 id={`${id}-heading`}>Thompson distributions</h4>
          <span className="belief-chart-note">{visible.length} of {valid.length} shown · axes scale automatically</span>
        </div>
        <p id={`${id}-description`} className="belief-chart-note">
          Plausible CPC draws from the current normal distributions. Pending results narrow the sampling
          uncertainty (σ); the table’s ± shows belief uncertainty before that adjustment. Curve height is
          probability density, not the chance of winning. Only networks covering a click’s merchant compete
          for it.
        </p>
      </figcaption>
      <div ref={container} className="belief-chart-plot">
        {bounds && xTicks ? (
          <svg
            viewBox={`0 0 ${width} ${HEIGHT}`}
            role="img"
            aria-labelledby={`${id}-title`}
            aria-describedby={`${id}-description`}
          >
            <title id={`${id}-title`}>{`Thompson sampling distributions for ${plural(visible.length, 'network')}`}</title>
            <defs>
              <clipPath id={`${id}-clip`}>
                <rect x={LEFT} y={TOP} width={plotWidth} height={BOTTOM - TOP} />
              </clipPath>
            </defs>
            <text x={LEFT} y={16} className="belief-axis-label">Probability density</text>
            {yTicks.map((value) => (
              <g key={value}>
                <line x1={LEFT} x2={width - RIGHT} y1={yAt(value)} y2={yAt(value)} className="belief-grid-line" />
                <text x={LEFT - 10} y={yAt(value) + 4} textAnchor="end" className="belief-tick-label">
                  {densityLabel(value)}
                </text>
              </g>
            ))}
            {xTicks.values.map((value) => (
              <g key={value}>
                <line x1={xAt(value)} x2={xAt(value)} y1={TOP} y2={BOTTOM}
                  className={value === 0 ? 'belief-zero-line' : 'belief-grid-line'} />
                <text x={xAt(value)} y={BOTTOM + 22} textAnchor="middle" className="belief-tick-label">
                  {formatCpcTick(value, xTicks.step)}
                </text>
              </g>
            ))}
            <line x1={LEFT} x2={width - RIGHT} y1={BOTTOM} y2={BOTTOM} className="belief-zero-line" />
            <g clipPath={`url(#${id}-clip)`}>
              {visible.map((row) => {
                const style = styles.get(row.network.id)
                const point = isPointDistribution(row)
                const path = distributionPoints(row)
                  .map(({ x, y }, index) => `${index === 0 ? 'M' : 'L'}${xAt(x)},${yAt(y)}`)
                  .join(' ')
                const peakY = point ? TOP + 3 : yAt(normalDensity(row.mean, row))
                return (
                  <g
                    key={row.network.id}
                    data-network-id={row.network.id}
                    className="belief-curve"
                    style={{
                      color: style?.color,
                      opacity: activeHighlight && activeHighlight !== row.network.id ? 0.25 : 1,
                    }}
                  >
                    <title>{describeDistribution(row)}</title>
                    {path ? (
                      <>
                        <path
                          d={`${path} L${xAt(row.mean + 4 * row.sd)},${BOTTOM} L${xAt(row.mean - 4 * row.sd)},${BOTTOM} Z`}
                          fill="currentColor" opacity={0.07}
                        />
                        <path d={path} fill="none" stroke="currentColor" strokeDasharray={style?.dash}
                          strokeWidth={activeHighlight === row.network.id ? 3 : 2} />
                      </>
                    ) : null}
                    <line x1={xAt(row.mean)} x2={xAt(row.mean)} y1={BOTTOM} y2={peakY}
                      stroke="currentColor" opacity={0.45} />
                    <circle cx={xAt(row.mean)} cy={peakY} r={4} fill="currentColor"
                      stroke="var(--color-bg)" strokeWidth={2} />
                  </g>
                )
              })}
            </g>
            <text x={LEFT + plotWidth / 2} y={HEIGHT - 7} textAnchor="middle" className="belief-axis-label">
              Plausible CPC ($ per click)
            </text>
          </svg>
        ) : (
          <div className="belief-chart-empty">
            {valid.length === 0
              ? 'No distributions available yet.'
              : visible.length === 0
                ? 'All distributions are hidden. Select a network below to show it.'
                : 'These distributions are outside the supported plotting range.'}
          </div>
        )}
      </div>
      {pointMass ? (
        <p className="belief-chart-note">
          A distribution with effectively zero uncertainty is shown as a vertical marker; it has no finite density peak.
        </p>
      ) : null}
      {unavailable > 0 ? (
        <p className="belief-chart-note">
          {plural(unavailable, 'distribution')} left out: the values cannot be plotted.
        </p>
      ) : null}
      <div className="belief-chart-legend" role="group" aria-label="Visible network distributions">
        {series.map((row, index) => {
          const available = isValidDistribution(row)
          const shown = available && !hidden.has(row.network.id)
          const style = styles.get(row.network.id)
          return (
            <button
              type="button"
              key={row.network.id}
              className="belief-legend-item"
              aria-label={`${row.network.name} distribution`}
              aria-describedby={`${id}-legend-${index}`}
              title={available ? describeDistribution(row) : 'Distribution unavailable'}
              aria-pressed={shown}
              disabled={!available}
              onClick={() => onToggle(row.network.id)}
              onMouseEnter={() => setHovered(row.network.id)}
              onMouseLeave={() => setHovered(null)}
              onFocus={() => setFocused(row.network.id)}
              onBlur={() => setFocused(null)}
            >
              <svg className="belief-legend-swatch" viewBox="0 0 28 8" aria-hidden="true" style={{ color: style?.color }}>
                <line x1={0} x2={28} y1={4} y2={4} stroke="currentColor" strokeWidth={3} strokeDasharray={style?.dash} />
              </svg>
              <span>
                <span className="belief-legend-name">
                  {row.network.name}{row.belief.observations === 0 ? ' (prior)' : ''}
                </span>
                <span id={`${id}-legend-${index}`} className="belief-legend-values">
                  {available
                    ? `${rate(row.mean)} · σ ${rate(row.sd)} · ${count(row.belief.inFlight)} pending`
                    : 'Distribution unavailable'}
                </span>
              </span>
            </button>
          )
        })}
      </div>
      <p className="belief-chart-note belief-legend-hint">
        Select a network to show or hide its curve. Hover or focus to highlight it.
      </p>
    </figure>
  )
}
