import { describe, expect, it, vi } from 'vitest'
import { AffiliateServiceClient } from './client'

const config = {
  serverUrl: 'https://affiliate.example.test',
  appKey: 'ak_test',
  appSecret: 'as_test',
}

describe('route outcome errors', () => {
  it('reads the rejection discriminator returned by the route endpoint', async () => {
    const fetchImpl = vi.fn(async () =>
      new Response(
        JSON.stringify({
          outcome: 'rejected',
          clickId: 'clk_one',
          networkId: 'net_miss',
          routedAt: '2026-08-18T12:00:00.000Z',
          alreadyRouted: true,
          errorCode: 'MERCHANT_NOT_SUPPORTED',
          message: 'The click was spent.',
        }),
        { status: 422, headers: { 'Content-Type': 'application/json' } },
      ),
    ) as unknown as typeof fetch
    const client = new AffiliateServiceClient(config, fetchImpl)

    await expect(client.route('clk_one', 'net_miss')).rejects.toMatchObject({
      status: 422,
      code: 'MERCHANT_NOT_SUPPORTED',
    })
  })
})
