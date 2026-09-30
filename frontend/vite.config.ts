import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

/**
 * No credentials here any more.
 *
 * The original client inlined APP_KEY / APP_SECRET into the bundle. They now live in the .NET host
 * (backend/README.md), which is the only thing that talks to the Affiliate Service. The browser
 * talks to the host on the same origin with a cookie session, so nothing sensitive ever reaches
 * `import.meta.env`, and `envPrefix` is gone with it.
 *
 * In development the host runs on 5175 and Vite proxies the three paths it owns -- the API, the
 * login/logout pages and the health endpoint -- so the browser sees one origin, exactly as in
 * production where the host serves `dist/` itself with an `index.html` fallback.
 */
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '.')
  const proxyTarget = env.VITE_API_PROXY_TARGET || 'http://localhost:5175'

  return {
    plugins: [react(), tailwindcss()],
    server: {
      /*
       * 5174, pinned. Vite's default is 5173, which the Affiliate Service's admin console claims with
       * `strictPort` — so without this, running both locally means whichever starts second silently
       * lands on a different port, or fails to start. `strictPort` here makes that a clear error rather
       * than a puzzle about why the API is rejecting the origin.
       */
      port: 5174,
      strictPort: true,
      proxy: {
        '/api': { target: proxyTarget },
        '/authentication': { target: proxyTarget },
        '/status': { target: proxyTarget },
      },
    },
  }
})
