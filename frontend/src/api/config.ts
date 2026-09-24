/**
 * Configuration, read once at startup.
 *
 * Vite inlines these into the browser bundle, so APP_SECRET is visible to anyone with devtools.
 * That is a known trade-off for a local-only exercise, not an oversight -- see the note in
 * vite.config.ts. Moving the loop and the credentials into a small local process is a legitimate
 * improvement.
 *
 * Missing configuration fails LOUDLY at startup rather than producing a stream of 401s that look
 * like a server problem.
 */
export interface Config {
  appKey: string
  appSecret: string
  serverUrl: string
}

function required(name: string, value: string | undefined): string {
  if (value === undefined || value.trim() === '') {
    throw new Error(
      `${name} is not set. Copy .env.example to .env and paste in the credentials you were sent, ` +
        `then restart the dev server -- Vite only reads .env at startup.`,
    )
  }
  return value.trim()
}

export function readConfig(env: Record<string, string | undefined> = import.meta.env): Config {
  return {
    appKey: required('APP_KEY', env.APP_KEY),
    appSecret: required('APP_SECRET', env.APP_SECRET),
    // Trailing slash trimmed so `${serverUrl}/clicks/next` cannot become a double slash, which
    // some routers treat as a different path.
    serverUrl: required('SERVER_URL', env.SERVER_URL).replace(/\/+$/, ''),
  }
}
