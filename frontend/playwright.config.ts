/**
 * End-to-end tests for the optimizer client, against a real Affiliate Service.
 *
 * The client is the candidate-facing half, so these tests do what a candidate does: point it at the
 * server, let the loop run, and check that clicks route and commissions arrive.
 *
 * Credentials come from `.env`, the same file the client reads — so this suite needs a candidate to
 * have been issued and pasted in, exactly as a candidate would. The spec says so plainly when they
 * are missing rather than failing with a wall of 401s.
 *
 * Not parallel: every worker would share one credential pair, and the pair is rationed at 20
 * clicks/min. Two workers racing for that ration would produce 429s that look like client bugs.
 */
import { defineConfig } from '@playwright/test'

const CLIENT_URL = 'http://localhost:5174'

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: 0,
  reporter: process.env.CI ? 'list' : [['list'], ['html', { open: 'never' }]],

  use: {
    baseURL: CLIENT_URL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },

  webServer: {
    // Port 5174 so this never collides with the admin console's 5173.
    command: 'npm run dev -- --port 5174 --strictPort',
    url: CLIENT_URL,
    reuseExistingServer: true,
    timeout: 60_000,
    stdout: 'ignore',
    stderr: 'pipe',
  },
})
