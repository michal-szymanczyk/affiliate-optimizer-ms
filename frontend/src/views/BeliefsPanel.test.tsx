import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import type { Network } from '../api/client'
import type { NetworkBeliefView } from '../models/thompson'
import { BeliefsPanel } from './BeliefsPanel'

afterEach(cleanup)

const networks: Network[] = [
  { id: 'net-a', name: 'Alpha', advertisedRate: 0.4, supportedMerchantIds: [1] },
  { id: 'net-b', name: 'Bravo', advertisedRate: 0.6, supportedMerchantIds: [1] },
]
const beliefs: NetworkBeliefView[] = networks.map((network) => ({
  networkId: network.id, estimate: network.advertisedRate, uncertainty: 0.2, samplingUncertainty: 0.02,
  inFlight: 2, usual: network.advertisedRate, usualSpread: 0.2, newestResultAt: null, observations: 0,
}))
const summary = { routed: 0, decided: 0, exploratory: 0, shareByNetwork: new Map<string, number>() }
const props = { networks, beliefs, summary, deciding: true, now: 1_700_000_000_000 }

function showChart() { fireEvent.click(screen.getByRole('button', { name: 'Show distributions' })) }

describe('optional Thompson distributions', () => {
  it('defaults to the table, mounts the graph on demand, and keeps the table when hidden', () => {
    const { container } = render(<BeliefsPanel {...props} />)
    expect(screen.queryByRole('img')).toBeNull()
    expect(screen.getByRole('table')).toBeDefined()
    showChart()
    expect(screen.getByRole('button', { name: 'Hide distributions' }).getAttribute('aria-expanded')).toBe('true')
    expect(screen.getByRole('img', { name: /Thompson sampling distributions for 2 networks/ })).toBeDefined()
    expect(container.querySelectorAll('.belief-curve')).toHaveLength(2)
    expect(screen.getByRole('table')).toBeDefined()
    fireEvent.click(screen.getByRole('button', { name: 'Hide distributions' }))
    expect(container.querySelector('svg')).toBeNull()
    expect(screen.getByRole('table')).toBeDefined()
  })

  it('labels priors and distinguishes sampling uncertainty from the table', () => {
    render(<BeliefsPanel {...props} />)
    showChart()
    expect(screen.getByText('Alpha (prior)')).toBeDefined()
    expect(screen.getByText('$0.4000 · σ $0.0200 · 2 pending')).toBeDefined()
    expect(screen.getByText(/table’s ± shows belief uncertainty/)).toBeDefined()
  })

  it('allows every curve to be hidden and restored through accessible legend buttons', () => {
    const { container } = render(<BeliefsPanel {...props} />)
    showChart()
    const alpha = screen.getByRole('button', { name: 'Alpha distribution' })
    const bravo = screen.getByRole('button', { name: 'Bravo distribution' })
    fireEvent.click(alpha)
    expect(alpha.getAttribute('aria-pressed')).toBe('false')
    expect(container.querySelector('[data-network-id="net-a"]')).toBeNull()
    fireEvent.click(bravo)
    expect(screen.getByText(/All distributions are hidden/)).toBeDefined()
    fireEvent.click(alpha)
    expect(alpha.getAttribute('aria-pressed')).toBe('true')
    expect(container.querySelectorAll('.belief-curve')).toHaveLength(1)
  })

  it('remembers which curves were hidden, and the scale, while the chart is closed', () => {
    render(<BeliefsPanel {...props} />)
    showChart()
    fireEvent.click(screen.getByRole('button', { name: 'Alpha distribution' }))
    fireEvent.click(screen.getByRole('checkbox', { name: 'Compare densities on one scale' }))
    fireEvent.click(screen.getByRole('button', { name: 'Hide distributions' }))
    showChart()
    expect(screen.getByRole('button', { name: 'Alpha distribution' }).getAttribute('aria-pressed')).toBe('false')
    expect(screen.getByRole('button', { name: 'Bravo distribution' }).getAttribute('aria-pressed')).toBe('true')
    expect(screen.getByRole('img', { name: /distributions for 1 network$/ })).toBeDefined()
    const scale = screen.getByRole('checkbox', { name: 'Compare densities on one scale' }) as HTMLInputElement
    expect(scale.checked).toBe(true)
  })

  it('draws every curve to its own peak unless densities are compared on one scale', () => {
    // Alpha is ten times as sure as Bravo, so on one density scale Bravo is a tenth of its height.
    const { container } = render(<BeliefsPanel {...props} beliefs={[
      { ...beliefs[0]!, samplingUncertainty: 0.02 }, { ...beliefs[1]!, samplingUncertainty: 0.2 },
    ]} />)
    showChart()
    const peakY = (networkId: string) =>
      Number(container.querySelector(`[data-network-id="${networkId}"] circle`)!.getAttribute('cy'))
    const baseline = 246
    expect(peakY('net-a')).toBe(peakY('net-b'))
    expect(screen.getByText(/drawn to its own peak/)).toBeDefined()
    fireEvent.click(screen.getByRole('checkbox', { name: 'Compare densities on one scale' }))
    expect((baseline - peakY('net-a')) / (baseline - peakY('net-b'))).toBeCloseTo(10, 0)
    expect(screen.getByText(/Curve height is probability density/)).toBeDefined()
  })

  it('keeps a hovered highlight apart from a focused one, and counts in the singular', () => {
    const { container } = render(<BeliefsPanel {...props} beliefs={beliefs.map((belief) => ({ ...belief, inFlight: 1 }))} />)
    showChart()
    const alpha = screen.getByRole('button', { name: 'Alpha distribution' })
    const bravo = screen.getByRole('button', { name: 'Bravo distribution' })
    const dimmed = (networkId: string) =>
      (container.querySelector(`[data-network-id="${networkId}"]`) as SVGGElement).style.opacity === '0.25'
    expect(alpha.getAttribute('title')).toMatch(/, 1 pending result$/)
    fireEvent.focus(alpha)
    fireEvent.mouseEnter(bravo)
    expect(dimmed('net-a')).toBe(true)
    // The mouse leaves: the highlight goes back to the button that still has focus.
    fireEvent.mouseLeave(bravo)
    expect(dimmed('net-a')).toBe(false)
    expect(dimmed('net-b')).toBe(true)
    fireEvent.blur(alpha)
    expect(dimmed('net-b')).toBe(false)
  })

  it('updates curves and labels after results or time change, retaining network colors', () => {
    const { container, rerender } = render(<BeliefsPanel {...props} />)
    showChart()
    const oldCurve = container.querySelector('[data-network-id="net-a"]')!
    const oldPath = oldCurve.querySelector('path')!.getAttribute('d')
    const oldColor = oldCurve.getAttribute('style')
    rerender(<BeliefsPanel {...props} now={props.now + 60_000} beliefs={beliefs.map((belief) => ({
      ...belief, estimate: 0.8, samplingUncertainty: 0.1, inFlight: 0, observations: 1, newestResultAt: props.now,
    }))} />)
    const curve = container.querySelector('[data-network-id="net-a"]')!
    expect(curve.querySelector('path')!.getAttribute('d')).not.toBe(oldPath)
    expect(curve.getAttribute('style')).toBe(oldColor)
    expect(screen.queryByText('Alpha (prior)')).toBeNull()
    expect(screen.getAllByText('$0.8000 · σ $0.1000 · 0 pending')).toHaveLength(2)
    expect(curve.querySelector('path')!.getAttribute('d')).not.toMatch(/NaN|Infinity/)
  })

  it('handles no beliefs, invalid distributions, and zero uncertainty', () => {
    const { container, rerender } = render(<BeliefsPanel {...props} beliefs={[]} />)
    showChart()
    expect(screen.getByText('No distributions available yet.')).toBeDefined()
    rerender(<BeliefsPanel {...props} beliefs={[
      { ...beliefs[0]!, samplingUncertainty: NaN }, { ...beliefs[1]!, samplingUncertainty: 0 },
    ]} />)
    expect(screen.getByRole('button', { name: 'Alpha distribution' }).hasAttribute('disabled')).toBe(true)
    expect(screen.getByText(/effectively zero uncertainty/)).toBeDefined()
    const curve = container.querySelector('[data-network-id="net-b"]')!
    expect(curve.querySelector('path')).toBeNull()
    expect(curve.querySelector('line')).not.toBeNull()
    expect(container.querySelector('svg')!.innerHTML).not.toMatch(/NaN|Infinity/)
  })
})
