import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

/**
 * Credentials are read from .env and exposed to the client bundle.
 *
 * Note this is a known trade-off, not an oversight: Vite inlines these into browser
 * JavaScript, so APP_SECRET is visible to anyone with devtools. Acceptable for a local-only
 * exercise; moving the loop and the credentials into a small local process is a legitimate
 * improvement to make.
 */
export default defineConfig({
  plugins: [react(), tailwindcss()],
  envPrefix: ['APP_', 'SERVER_'],
  server: {
    /*
     * 5174, pinned. Vite's default is 5173, which the Affiliate Service's admin console claims with
     * `strictPort` — so without this, running both locally means whichever starts second silently
     * lands on a different port, or fails to start. `strictPort` here makes that a clear error rather
     * than a puzzle about why the API is rejecting the origin.
     *
     * The server's `ALLOWED_ORIGINS` permits this port on both localhost and 127.0.0.1.
     */
    port: 5174,
    strictPort: true,
  },
})
