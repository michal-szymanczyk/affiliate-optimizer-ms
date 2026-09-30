/**
 * The operator UI, end to end against the .NET host and a live Affiliate Service.
 *
 * What these assert is that the operator can see the loop close: a run starts from the UI, clicks
 * appear in the feed, route to a network, settle a minute later with a commission the tiles reflect,
 * and the controls do what their labels say. Everything the host owns -- pacing, retries, cursors,
 * filtering, sorting, the statistics -- is tested in backend/Tests and is deliberately not repeated
 * here.
 *
 * The one-minute disclosure delay is real: this suite cannot move the server's clock, so the
 * settlement test waits, with a generous timeout, and is the only slow test in the file.
 *
 * The session comes from e2e/global-setup.ts (storageState), so no test signs in itself.
 */
import { expect, test, type Page } from '@playwright/test'

const DISCLOSURE_TIMEOUT_MS = 90_000

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

/** How many clicks the run has issued. The right progress signal: the TABLE shows one page of 40. */
async function processed(page: Page): Promise<number> {
  const text = await counter(page, 'clicks-processed')
  return text === '—' ? 0 : Number(text.replace(/,/g, ''))
}

const RUN_STATUS = '[data-run-status]'
const ROWS = 'table.table tbody tr[data-click-id]'

/**
 * Makes sure a run is issuing clicks: starts a `random` run when none is active, resumes a paused
 * one. Every test needs traffic, and a previous test (or a previous session) may have left the host
 * in either state.
 */
async function ensureRunning(page: Page): Promise<void> {
  const status = page.locator(RUN_STATUS)
  await expect(status).toHaveText(/issuing · polling|paused · polling continues|no active run/, { timeout: 15_000 })

  const text = ((await status.textContent()) ?? '').trim()
  if (text === 'no active run') {
    // The checkbox's accessible name is its label: the model name first, then version and description.
    await page.getByRole('checkbox', { name: /^random / }).check()
    await page.getByRole('button', { name: 'Start run' }).click()
  } else if (text.startsWith('paused')) {
    await page.getByRole('button', { name: 'Resume loop' }).click()
  }

  // Starting fetches the catalogues from the Affiliate Service first, so allow it a moment.
  await expect(status).toHaveText('issuing · polling', { timeout: 30_000 })
  await expect(page.getByText('The client cannot start')).toBeHidden()
}

test.beforeEach(async ({ page }) => {
  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'Click feed' })).toBeVisible()
  await ensureRunning(page)
})

test('the operator is signed in, sees the active run and its counters', async ({ page }) => {
  await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible()
  await expect(page.locator(RUN_STATUS)).toHaveText('issuing · polling')
  await expect(page.locator('[data-model-name="random"]')).toBeVisible()

  for (const id of ['running-for', 'clicks-processed', 'clicks-completed', 'commission']) {
    await expect(page.locator(`[data-counter="${id}"]`)).toBeVisible()
  }
  await expect
    .poll(async () => counter(page, 'running-for'), { timeout: 10_000 })
    .toMatch(/^\d+m \d{2}s$|^\d+h \d{2}m$/)

  // Health is read from the host and localises faults; the badge is always there to read.
  await expect(page.locator('[data-diagnosis]')).toBeVisible()
})

test('clicks appear in the feed and get routed to a named network', async ({ page }) => {
  // Paced at one every three seconds, so a handful arrive quickly.
  const rows = page.locator(ROWS)
  await expect
    .poll(async () => rows.count(), { timeout: 20_000, message: 'the host should be issuing clicks' })
    .toBeGreaterThanOrEqual(3)

  await expect(page.locator(`${ROWS}[data-click-state="routed"]`).first()).toBeVisible({ timeout: 20_000 })

  // A routed row names its network and the model that decided.
  const routed = page.locator(`${ROWS}[data-click-state="routed"]`).first()
  await expect(routed.locator('td').nth(3)).toHaveText('random')
  await expect(routed.locator('td').nth(4)).not.toHaveText('—')
  await expect(routed.locator('td').nth(6)).toHaveText('—')
})

test('the "why" expander explains a decision', async ({ page }) => {
  const routed = page.locator(`${ROWS}[data-click-state="routed"], ${ROWS}[data-click-state="settled"]`).first()
  await expect(routed).toBeVisible({ timeout: 20_000 })
  const clickId = await routed.getAttribute('data-click-id')

  await routed.getByRole('button', { name: /^Why / }).click()
  const why = page.locator(`[data-why-row="${clickId}"]`)
  await expect(why).toBeVisible()
  await expect(why).toContainText(/chose|No decision was recorded/)
})

test('a routed click settles with a commission the tiles reflect', async ({ page }) => {
  test.setTimeout(DISCLOSURE_TIMEOUT_MS + 60_000)

  await expect(page.locator(`${ROWS}[data-click-state="routed"]`).first()).toBeVisible({ timeout: 20_000 })

  // Then it settles, for real, on the server's own one-minute delay.
  await expect(page.locator(`${ROWS}[data-click-state="settled"]`).first()).toBeVisible({
    timeout: DISCLOSURE_TIMEOUT_MS,
  })
  await expect
    .poll(async () => counter(page, 'commission'), {
      timeout: 20_000,
      message: 'the commission tile should follow the first settlement',
    })
    .not.toBe('$0.00')

  expect(await counter(page, 'commission')).toMatch(/^\$\d+\.\d{2}$/)
  expect(await counter(page, 'clicks-completed')).not.toBe('0')
  // The per-click average is only meaningful once something settled.
  await expect(page.getByText(/per settled click/)).toBeVisible()

  // A settled row shows its four-decimal commission.
  const settled = page.locator(`${ROWS}[data-click-state="settled"]`).first()
  await expect(settled.locator('td').nth(6)).toHaveText(/^\$\d+\.\d{4}$/)
})

test('pausing stops new clicks but keeps polling; resuming grows the counter again', async ({ page }) => {
  test.setTimeout(60_000)
  await expect.poll(async () => processed(page), { timeout: 20_000 }).toBeGreaterThanOrEqual(2)

  await page.getByRole('button', { name: 'Pause loop' }).click()
  await expect(page.locator(RUN_STATUS)).toHaveText('paused · polling continues')

  // A click already in flight when the pause landed may still be counted; let it settle first.
  await page.waitForTimeout(4_000)
  const frozen = await processed(page)
  // Well over the three-second click interval: nothing new may be issued.
  await page.waitForTimeout(8_000)
  expect(await processed(page)).toBe(frozen)

  await page.getByRole('button', { name: 'Resume loop' }).click()
  await expect(page.locator(RUN_STATUS)).toHaveText('issuing · polling')
  await expect.poll(async () => processed(page), { timeout: 20_000 }).toBeGreaterThan(frozen)
})

test('the filters narrow the feed on the server and the range line follows', async ({ page }) => {
  const rows = page.locator(ROWS)
  await expect.poll(async () => rows.count(), { timeout: 20_000 }).toBeGreaterThanOrEqual(3)

  // Paused so rows cannot arrive between reading a merchant name and asserting on the filtered set.
  await page.getByRole('button', { name: 'Pause loop' }).click()
  await expect(page.locator(RUN_STATUS)).toHaveText('paused · polling continues')

  const merchantCell = (await rows.first().locator('td').nth(2).textContent()) ?? ''
  const merchantName = merchantCell.split('·')[0]?.trim() ?? ''
  expect(merchantName.length).toBeGreaterThan(0)

  await page.getByLabel('Merchant', { exact: true }).fill(merchantName)
  await expect(page.locator('[data-feed-range]')).toBeVisible()
  await expect.poll(async () => rows.count(), { timeout: 10_000 }).toBeGreaterThanOrEqual(1)
  for (let i = 0; i < (await rows.count()); i += 1) {
    await expect(rows.nth(i).locator('td').nth(2)).toContainText(merchantName)
  }

  // The state filter is a query parameter too: every row the host returns carries that state.
  await page.getByRole('button', { name: 'Clear' }).click()
  await page.getByLabel('State', { exact: true }).selectOption('routed')
  await expect.poll(async () => rows.count(), { timeout: 10_000 }).toBeGreaterThanOrEqual(1)
  for (let i = 0; i < (await rows.count()); i += 1) {
    await expect(rows.nth(i)).toHaveAttribute('data-click-state', 'routed')
  }

  // "Errors only" is about failure (rejected or expired), not about carrying a code: with the random
  // model over public coverage there should be none, and an empty result is the correct answer.
  await page.getByRole('button', { name: 'Clear' }).click()
  await page.getByText('Errors only').click()
  await expect(page.getByText(/No clicks match these filters|Showing \d+–\d+ of \d+/)).toBeVisible()
  for (let i = 0; i < (await rows.count()); i += 1) {
    await expect(rows.nth(i)).toHaveAttribute('data-click-state', /rejected|expired/)
  }
})

test('sorting a column reorders the feed on the server', async ({ page }) => {
  const rows = page.locator(ROWS)
  await expect.poll(async () => rows.count(), { timeout: 20_000 }).toBeGreaterThanOrEqual(4)

  await page.getByRole('button', { name: 'Sort by Issued' }).click()
  await expect(page.getByText(/sorted by issuedAt ascending/)).toBeVisible()

  // Ascending: oldest first, which is the reverse of the default view. Read once the sorted page is in.
  await expect
    .poll(
      async () => {
        const cells = page.locator(`${ROWS} td:nth-child(2)`)
        const times: string[] = []
        for (let i = 0; i < Math.min(await cells.count(), 5); i += 1) {
          times.push(((await cells.nth(i).textContent()) ?? '').trim())
        }
        return times.length >= 2 && times.every((time, index) => index === 0 || time >= (times[index - 1] ?? ''))
      },
      { timeout: 10_000, message: 'the first rows should be in ascending issue order' },
    )
    .toBe(true)
})

test('the Networks tab shows advertised against realized with the evidence legend', async ({ page }) => {
  await page.getByRole('tab', { name: 'Networks' }).click()
  await expect(page.getByRole('heading', { name: 'Networks' })).toBeVisible()
  await expect(page.getByRole('columnheader', { name: 'Advertised' })).toBeVisible()
  await expect(page.getByRole('columnheader', { name: 'Realized (95% CI)' })).toBeVisible()
  await expect.poll(async () => page.locator('tr[data-network-id]').count(), { timeout: 10_000 }).toBeGreaterThan(0)
  await expect(page.getByText(/fewer than 10 settled clicks/)).toBeVisible()
  await expect(page.getByText(/no disclosure in the last 5 minutes/)).toBeVisible()
  await expect(page.getByText(/1\.96 standard errors/)).toBeVisible()
})

test('the Compare tab renders a verdict that is never a bare winner', async ({ page }) => {
  await page.getByRole('tab', { name: 'Compare' }).click()
  await expect(page.getByRole('heading', { name: 'Compare' })).toBeVisible()

  const verdict = page.locator('[data-verdict]')
  await expect(verdict).toBeVisible({ timeout: 10_000 })
  await expect(verdict).toHaveAttribute('data-verdict', /inconclusive|noSeparation|leading/)
  // The caveats are part of the finding, whatever the verdict.
  await expect(verdict).toContainText('No counterfactual')
  await expect(page.locator('tr[data-arm="random"]')).toBeVisible()
})

test('the Runs tab lists the active run as the one in scope', async ({ page }) => {
  await page.getByRole('tab', { name: 'Runs' }).click()
  await expect(page.getByRole('heading', { name: 'Runs' })).toBeVisible()
  await expect.poll(async () => page.locator('tr[data-run-id]').count(), { timeout: 10_000 }).toBeGreaterThan(0)

  const scope = await page.locator('[data-scope]').getAttribute('data-scope')
  expect(scope).toMatch(/^\d+$/)
  await expect(page.locator(`tr[data-run-id="${scope}"]`).getByText('viewing')).toBeVisible()
})
