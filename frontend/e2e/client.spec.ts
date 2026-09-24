/**
 * The optimizer client, end to end against a live Affiliate Service.
 *
 * What these assert is that the loop actually closes: clicks are pulled, routed to networks that
 * cover their merchant, and their commissions appear a minute later with a running total that
 * matches. That is what a candidate must be able to trust before changing optimization models.
 *
 * The one-minute disclosure delay is real here — this suite has no way to move the server's clock,
 * because the client is candidate-facing and a candidate cannot move it either. So the disclosure
 * test waits, with a generous timeout, and is the only slow test in the file.
 */
import { expect, test, type Page } from '@playwright/test'
import { readFileSync } from 'node:fs'

const DISCLOSURE_TIMEOUT_MS = 90_000

interface ClientEnv {
  appKey: string
  appSecret: string
  serverUrl: string
}

/** Reads the same `.env` the client reads, so the suite and the app cannot disagree. */
function readEnv(): ClientEnv {
  let raw: string
  try {
    raw = readFileSync('.env', 'utf8')
  } catch {
    throw new Error(
      'No .env found. Copy .env.example to .env and paste in the credentials you were issued — ' +
        'this suite drives the real API, exactly as the client does.',
    )
  }

  const values = new Map<string, string>()
  for (const line of raw.split('\n')) {
    const match = /^([A-Z_]+)=(.*)$/.exec(line.trim())
    if (match?.[1] && match[2]) values.set(match[1], match[2].trim())
  }

  const appKey = values.get('APP_KEY')
  const appSecret = values.get('APP_SECRET')
  const serverUrl = values.get('SERVER_URL')
  if (!appKey || !appSecret || !serverUrl) {
    throw new Error('APP_KEY, APP_SECRET and SERVER_URL must all be set in .env before running this suite.')
  }
  return { appKey, appSecret, serverUrl }
}

const env = readEnv()

/**
 * One counter tile's value, read through its stable `data-counter` key.
 *
 * Keyed on the machine id, not the visible kicker: Industry uppercases the kicker in CSS, so the
 * rendered text and the source string differ, and keying off either would couple this suite to copy.
 */
type CounterId = 'running-for' | 'clicks-processed' | 'clicks-completed' | 'commission'

async function counter(page: Page, id: CounterId): Promise<string> {
  const value = page.locator(`[data-counter="${id}"] [data-counter-value]`)
  await expect(value, `the "${id}" counter should be on screen`).toBeVisible()
  return ((await value.textContent()) ?? '').trim()
}

/** How many clicks the loop has issued. The right progress signal: the TABLE caps at 40 rows. */
async function processed(page: Page): Promise<number> {
  return Number((await counter(page, 'clicks-processed')).replace(/,/g, ''))
}

test.beforeEach(async ({ page }) => {
  await page.goto('/')
  // The loop starts by fetching merchants and networks; the feed heading appears once it has.
  await expect(page.getByRole('heading', { name: 'Click feed' })).toBeVisible()
})

test('the client starts, authenticates, and begins polling', async ({ page }) => {
  await expect(page.getByText('polling')).toBeVisible()
  // No fatal banner: credentials and the server URL are good.
  await expect(page.getByText('The client cannot start')).toBeHidden()

  for (const id of ['running-for', 'clicks-processed', 'clicks-completed', 'commission']) {
    await expect(page.locator(`[data-counter="${id}"]`)).toBeVisible()
  }
  expect(await counter(page, 'running-for')).toMatch(/^\d+m \d{2}s$/)
})

test('it pulls clicks and routes each one to a network that covers its merchant', async ({ page }) => {
  // Paced at one every three seconds, so a handful arrive quickly.
  const rows = page.locator('table.table tbody tr')
  await expect
    .poll(async () => rows.count(), { timeout: 20_000, message: 'the loop should be issuing clicks' })
    .toBeGreaterThanOrEqual(3)

  // Every routed row names a network, and that network genuinely covers the merchant. Coverage is
  // public, so an optimization model routing outside it would lose clicks to an avoidable mistake.
  const response = await page.request.get(`${env.serverUrl}/networks`, {
    headers: { 'X-App-Key': env.appKey, 'X-App-Secret': env.appSecret },
  })
  expect(response.status()).toBe(200)
  const { networks } = (await response.json()) as {
    networks: { id: string; name: string; supportedMerchantIds: number[] }[]
  }
  const coverageByName = new Map(networks.map((network) => [network.name, network.supportedMerchantIds]))

  const count = Math.min(await rows.count(), 5)
  for (let i = 0; i < count; i += 1) {
    const cells = rows.nth(i).locator('td')
    const merchantCell = (await cells.nth(2).textContent()) ?? ''
    const networkName = ((await cells.nth(3).textContent()) ?? '').trim()
    if (networkName === '—') continue

    const merchantId = Number(/id (\d+)/.exec(merchantCell)?.[1])
    expect(Number.isInteger(merchantId), `could not read a merchant id from "${merchantCell}"`).toBe(true)
    expect(
      coverageByName.get(networkName),
      `${networkName} should cover merchant ${merchantId}`,
    ).toContain(merchantId)
  }
})

test('a routed click shows no commission until it is disclosed, then shows one', async ({ page }) => {
  test.setTimeout(DISCLOSURE_TIMEOUT_MS + 30_000)

  const rows = page.locator('table.table tbody tr')
  await expect.poll(async () => rows.count(), { timeout: 20_000 }).toBeGreaterThanOrEqual(1)

  // Immediately after routing, the commission column is withheld — the acknowledgment never
  // carries one, and the feed will not report it for a minute.
  await expect(rows.first().locator('td').nth(5)).toHaveText('—')
  await expect(rows.first().getByText('routed')).toBeVisible()

  // Then it settles, for real, on the server's own one-minute delay.
  await expect
    .poll(async () => counter(page, 'commission'), {
      timeout: DISCLOSURE_TIMEOUT_MS,
      message: 'commissions should appear about a minute after the first routing',
    })
    .not.toBe('$0.00')

  expect(await counter(page, 'commission')).toMatch(/^\$\d+\.\d{2}$/)
  expect(await counter(page, 'clicks-completed')).not.toBe('0')
  // The per-click average is only meaningful once something settled.
  await expect(page.getByText(/per settled click/)).toBeVisible()
})

test('the running total matches the sum of the settled rows on screen', async ({ page }) => {
  test.setTimeout(DISCLOSURE_TIMEOUT_MS + 30_000)

  await expect
    .poll(async () => counter(page, 'commission'), { timeout: DISCLOSURE_TIMEOUT_MS })
    .not.toBe('$0.00')

  /*
   * Pause first. The total is a moving target while the loop runs, and a click that settles between
   * reading the rows and reading the counter would make a correct client look wrong.
   */
  await page.getByRole('button', { name: 'Pause loop' }).click()
  await expect(page.getByText('paused')).toBeVisible()

  // `exact` because the sortable column headers carry the same accessible names as the filters.
  await page.getByLabel('State', { exact: true }).selectOption('settled')

  const cells = page.locator('table.table tbody tr td:nth-child(6)')
  const amounts: number[] = []
  for (let i = 0; i < (await cells.count()); i += 1) {
    const text = ((await cells.nth(i).textContent()) ?? '').replace('$', '').trim()
    amounts.push(Number(text))
  }
  expect(amounts.length).toBeGreaterThan(0)

  const summed = Math.round(amounts.reduce((total, amount) => total + amount, 0) * 100) / 100
  const displayed = Number((await counter(page, 'commission')).replace('$', ''))
  expect(displayed).toBeCloseTo(summed, 2)
})

test('pausing stops new clicks but keeps resolving the ones in flight', async ({ page }) => {
  // Measured on the PROCESSED counter, not on row count: the table caps at 40 rows, so once a run
  // is past that, counting rows would show no growth however hard the loop worked.
  await expect.poll(async () => processed(page), { timeout: 20_000 }).toBeGreaterThanOrEqual(2)

  await page.getByRole('button', { name: 'Pause loop' }).click()
  await expect(page.getByText('paused')).toBeVisible()

  const frozen = await processed(page)
  // Well over the three-second click interval: nothing new may be issued.
  await page.waitForTimeout(8_000)
  expect(await processed(page)).toBe(frozen)

  await page.getByRole('button', { name: 'Resume loop' }).click()
  await expect(page.getByText('polling')).toBeVisible()
  await expect.poll(async () => processed(page), { timeout: 20_000 }).toBeGreaterThan(frozen)
})

test('the filters narrow the feed and the summary follows', async ({ page }) => {
  const rows = page.locator('table.table tbody tr')
  await expect.poll(async () => rows.count(), { timeout: 20_000 }).toBeGreaterThanOrEqual(3)

  // Paused so rows cannot arrive between reading a merchant name and asserting on the filtered set.
  await page.getByRole('button', { name: 'Pause loop' }).click()
  await expect(page.getByText('paused')).toBeVisible()

  const merchantCell = (await rows.first().locator('td').nth(2).textContent()) ?? ''
  const merchantName = merchantCell.split('·')[0]?.trim() ?? ''
  expect(merchantName.length).toBeGreaterThan(0)

  await page.getByLabel('Merchant', { exact: true }).fill(merchantName)
  await expect(page.getByText(/\d+ clicks in view/)).toBeVisible()
  for (let i = 0; i < (await rows.count()); i += 1) {
    await expect(rows.nth(i).locator('td').nth(2)).toContainText(merchantName)
  }

  // "Errors only" is about failure, not about carrying a code: with a random optimization model
  // over public coverage there should be none, and an empty result is the correct answer.
  await page.getByRole('button', { name: 'Clear' }).click()
  await page.getByText('Errors only').click()
  await expect(page.getByText(/clicks in view/)).toBeVisible()
})

test('sorting a column reorders the feed', async ({ page }) => {
  const rows = page.locator('table.table tbody tr')
  await expect.poll(async () => rows.count(), { timeout: 20_000 }).toBeGreaterThanOrEqual(4)

  await page.getByRole('button', { name: 'Sort by Issued' }).click()

  const times: string[] = []
  const cells = page.locator('table.table tbody tr td:nth-child(2)')
  for (let i = 0; i < Math.min(await cells.count(), 5); i += 1) {
    times.push(((await cells.nth(i).textContent()) ?? '').trim())
  }
  // Ascending: oldest first, which is the reverse of the default view.
  expect(times).toEqual([...times].sort())
})

test('bad credentials are reported plainly rather than as a silent empty feed', async ({ page }) => {
  // The client reads credentials at build time, so this drives the API directly to prove the server
  // answers with a coded 401 — which is what the client turns into its non-dismissible strip.
  const response = await page.request.get(`${env.serverUrl}/networks`, {
    headers: { 'X-App-Key': 'ak_not_a_real_key', 'X-App-Secret': 'as_not_a_real_secret' },
  })
  expect(response.status()).toBe(401)
  expect((await response.json()).code).toBe('UNAUTHORIZED')
})
