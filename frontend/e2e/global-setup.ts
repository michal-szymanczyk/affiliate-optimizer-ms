/**
 * Signs the operator in once and saves the cookie for every test.
 *
 * The login page is the host's own Razor page, reached through the Vite proxy so the cookie lands on
 * the same origin the tests use. The default account is the one the host seeds in Development
 * (backend/AffiliateOptimizer.Web/appsettings.Development.json); override with OPERATOR_EMAIL and
 * OPERATOR_PASSWORD when the host is configured differently.
 */
import { chromium, type FullConfig } from '@playwright/test'
import { mkdir } from 'node:fs/promises'
import { dirname } from 'node:path'
import { STORAGE_STATE } from '../playwright.config'

export default async function globalSetup(config: FullConfig): Promise<void> {
  const baseURL = config.projects[0]?.use.baseURL ?? 'http://localhost:5174'
  const email = process.env.OPERATOR_EMAIL ?? 'operator@local'
  const password = process.env.OPERATOR_PASSWORD ?? 'operator-dev-password'

  const browser = await chromium.launch()
  try {
    const page = await browser.newPage()
    await page.goto(`${baseURL}/authentication/login`)
    await page.fill('#email', email)
    await page.fill('#password', password)
    await Promise.all([
      // The host answers the POST with a redirect to `/`, which the proxy hands back to Vite.
      page.waitForURL(`${baseURL}/`),
      page.click('button[type="submit"]'),
    ])

    await mkdir(dirname(STORAGE_STATE), { recursive: true })
    await page.context().storageState({ path: STORAGE_STATE })
  } finally {
    await browser.close()
  }
}
