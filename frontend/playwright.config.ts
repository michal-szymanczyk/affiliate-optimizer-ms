/**
 * End-to-end tests for the operator UI, against the real .NET host and, through it, a real
 * Affiliate Service.
 *
 * Two servers come up: the host on 5175 (which needs the Affiliate Service credentials configured
 * in backend/, see backend/README.md) and the Vite dev server on 5174, which proxies to it. The
 * host's `/status` is the readiness probe; it answers 503 until the Affiliate Service is reachable,
 * so a missing credential shows up here as a timeout with the host's own message on stderr rather
 * than as a wall of failing tests.
 *
 * Sign-in happens once, in `e2e/global-setup.ts`, and every test reuses the saved cookie.
 *
 * Not parallel: every worker would share one credential pair, and the pair is rationed at 20
 * clicks/min. Two workers racing for that ration would produce 429s that look like client bugs.
 */
import { defineConfig } from '@playwright/test'

const CLIENT_URL = 'http://localhost:5174'
const HOST_URL = 'http://localhost:5175'

/** Where global-setup saves the operator's session. Gitignored. */
export const STORAGE_STATE = 'e2e/.auth/operator.json'

export default defineConfig({
  testDir: './e2e',
  globalSetup: './e2e/global-setup.ts',
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: 0,
  reporter: process.env.CI ? 'list' : [['list'], ['html', { open: 'never' }]],

  use: {
    baseURL: CLIENT_URL,
    storageState: STORAGE_STATE,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },

  webServer: [
    {
      // The host first: the UI's proxy has nothing to forward to until it is up.
      command: 'dotnet run --project ../backend/AffiliateOptimizer.Web --no-launch-profile',
      url: `${HOST_URL}/status`,
      env: {
        ASPNETCORE_ENVIRONMENT: 'Development',
        ASPNETCORE_URLS: HOST_URL,
      },
      reuseExistingServer: true,
      // `dotnet run` compiles before it listens; a cold build needs the headroom.
      timeout: 120_000,
      stdout: 'ignore',
      stderr: 'pipe',
    },
    {
      // Port 5174 so this never collides with the admin console's 5173.
      command: 'npm run dev -- --port 5174 --strictPort',
      url: CLIENT_URL,
      reuseExistingServer: true,
      timeout: 60_000,
      stdout: 'ignore',
      stderr: 'pipe',
    },
  ],
})
