/**
 * The operator API client: the browser's only way to talk to the .NET host.
 *
 * Same origin, cookie session, plain `fetch`. The browser holds NO credentials for the Affiliate
 * Service any more -- those live in the host (backend/README.md) -- so there is nothing here to
 * inline into a bundle and nothing for devtools to leak.
 *
 * What this layer does NOT do is retry, back off, or decide policy. It performs one call and either
 * returns the typed response or throws an `OperatorApiError` that carries the host's status and, for
 * upstream failures, the Affiliate Service's own stable code. Deciding what to show belongs to the
 * views; deciding what to do about a throttle belongs to the loops, which now live in the host.
 *
 * Wire conventions (see `types.ts`): camelCase JSON, camelCase enum strings, ISO 8601 UTC timestamps,
 * and `serverNow` on every read response.
 */
import type {
  ClickFilter,
  ClicksResponse,
  CompareResponse,
  CountersResponse,
  CurrentRunResponse,
  HealthStatus,
  ListParams,
  ModelInfo,
  NetworkInsightResponse,
  RunsResponse,
  StartRunRequest,
  StartRunResponse,
  ValidationDetail,
} from './types'

export const LOGIN_PATH = '/authentication/login'
export const LOGOUT_PATH = '/authentication/logout'

/**
 * A failed call, normalised.
 *
 *   400  validation      message + `details` (one per field)
 *   401  not signed in   the client has already started the redirect to the login page
 *   403  not an operator
 *   404  unknown thing   message
 *   412  state conflict  message (a run is already active, an event is not dismissible, ...)
 *   502  upstream error  message + `code` (the Affiliate Service's own, e.g. RATE_LIMITED)
 *   503  unreachable     message + `code: 'TRANSPORT'`
 *   500  host fault      message
 *   0    no response     the host itself could not be reached (`code: 'NETWORK'`)
 */
export class OperatorApiError extends Error {
  readonly status: number
  readonly code?: string
  readonly details?: ValidationDetail[]
  readonly upstreamStatus?: number
  readonly retryAfterSeconds?: number

  constructor(
    status: number,
    message: string,
    extra: {
      code?: string
      details?: ValidationDetail[]
      upstreamStatus?: number
      retryAfterSeconds?: number
    } = {},
  ) {
    super(message)
    this.name = 'OperatorApiError'
    this.status = status
    if (extra.code !== undefined) this.code = extra.code
    if (extra.details !== undefined) this.details = extra.details
    if (extra.upstreamStatus !== undefined) this.upstreamStatus = extra.upstreamStatus
    if (extra.retryAfterSeconds !== undefined) this.retryAfterSeconds = extra.retryAfterSeconds
  }

  /** The Affiliate Service answered with an error, or could not be reached: "the client cannot start". */
  get isUpstream(): boolean {
    return this.status === 502 || this.status === 503
  }
}

export type QueryValue = string | number | boolean | null | undefined

/**
 * Builds a query string, omitting undefined, null and empty-string values.
 *
 * Omitting rather than sending `state=` matters: the host binds an absent parameter to null (no
 * filter) but an empty one to a binding error for enums and numbers.
 */
export function buildQuery(params: Record<string, QueryValue>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === '') continue
    search.set(key, String(value))
  }
  const encoded = search.toString()
  return encoded.length === 0 ? '' : `?${encoded}`
}

/** The login page, remembering where the operator was so the host can send them back. */
export function loginUrl(pathname: string, search: string): string {
  return `${LOGIN_PATH}?returnUrl=${encodeURIComponent(pathname + search)}`
}

interface ErrorBody {
  message?: unknown
  code?: unknown
  details?: unknown
  upstreamStatus?: unknown
  retryAfterSeconds?: unknown
}

function isValidationDetail(value: unknown): value is ValidationDetail {
  return (
    typeof value === 'object' &&
    value !== null &&
    typeof (value as ValidationDetail).fieldName === 'string' &&
    typeof (value as ValidationDetail).message === 'string'
  )
}

/** Turns a non-2xx status and whatever body came with it into one OperatorApiError. */
export function normalizeError(status: number, path: string, body: unknown): OperatorApiError {
  const raw: ErrorBody = typeof body === 'object' && body !== null ? (body as ErrorBody) : {}
  const message =
    typeof raw.message === 'string' && raw.message.length > 0 ? raw.message : `${path} returned ${status}.`
  const details = Array.isArray(raw.details) ? raw.details.filter(isValidationDetail) : undefined

  return new OperatorApiError(status, message, {
    ...(typeof raw.code === 'string' ? { code: raw.code } : {}),
    ...(details !== undefined && details.length > 0 ? { details } : {}),
    ...(typeof raw.upstreamStatus === 'number' ? { upstreamStatus: raw.upstreamStatus } : {}),
    ...(typeof raw.retryAfterSeconds === 'number' ? { retryAfterSeconds: raw.retryAfterSeconds } : {}),
  })
}

export interface OperatorApiOptions {
  /** Injected so the unit tests can drive this without a host. */
  fetchImpl?: typeof fetch
  /** What to do on a 401. The default sends the browser to the login page. */
  onUnauthorized?: () => void
}

function redirectToLogin(): void {
  window.location.assign(loginUrl(window.location.pathname, window.location.search))
}

/**
 * One client, one function per endpoint.
 *
 * The default `fetchImpl` is WRAPPED rather than passed by reference: `fetch` is a method on
 * `window`, and calling a stored reference invokes it with the wrong receiver, which the browser
 * rejects with "Illegal invocation".
 */
export function createOperatorApi(options: OperatorApiOptions = {}) {
  const fetchImpl: typeof fetch = options.fetchImpl ?? ((input, init) => globalThis.fetch(input, init))
  const onUnauthorized = options.onUnauthorized ?? redirectToLogin

  async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
    let response: Response
    try {
      response = await fetchImpl(path, {
        ...init,
        credentials: 'same-origin',
        headers: {
          Accept: 'application/json',
          ...(init.body === undefined ? {} : { 'Content-Type': 'application/json' }),
          ...init.headers,
        },
      })
    } catch (cause) {
      throw new OperatorApiError(
        0,
        `Could not reach the operator API at ${path}. Is the .NET host running? (${String(cause)})`,
        { code: 'NETWORK' },
      )
    }

    if (response.status === 401) {
      onUnauthorized()
      throw new OperatorApiError(401, 'Not signed in. Redirecting to the login page.', { code: 'UNAUTHENTICATED' })
    }

    const text = await response.text()
    let parsed: unknown = null
    if (text.length > 0) {
      try {
        parsed = JSON.parse(text)
      } catch {
        if (response.ok) {
          throw new OperatorApiError(
            response.status,
            `${path} returned a ${response.status} that is not JSON: ${text.slice(0, 200)}`,
            { code: 'UNREADABLE_RESPONSE' },
          )
        }
        // A non-JSON error page (a proxy's, say) still carries a status worth reporting.
        parsed = { message: `${path} returned ${response.status}: ${text.slice(0, 200)}` }
      }
    }

    if (!response.ok) throw normalizeError(response.status, path, parsed)
    return parsed as T
  }

  function get<T>(path: string, params: Record<string, QueryValue> = {}): Promise<T> {
    return request<T>(`${path}${buildQuery(params)}`)
  }

  function post<T>(path: string, body?: unknown): Promise<T> {
    return request<T>(path, {
      method: 'POST',
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    })
  }

  return {
    get,
    post,

    // ── runs ──
    getCurrentRun: () => get<CurrentRunResponse>('/api/runs/getcurrent'),
    getRuns: (params: ListParams = {}) => get<RunsResponse>('/api/runs/getall', { ...params }),
    startRun: (body: StartRunRequest) => post<StartRunResponse>('/api/runs/start', body),
    pauseRun: (runId: number) => post<void>('/api/runs/pause', { runId }),
    resumeRun: (runId: number) => post<void>('/api/runs/resume', { runId }),
    stopRun: (runId: number) => post<void>('/api/runs/stop', { runId }),

    // ── clicks ──
    getClicks: (filter: ClickFilter, params: ListParams = {}) =>
      get<ClicksResponse>('/api/clicks/getall', { ...filter, ...params }),
    getCounters: (runId: number | undefined) => get<CountersResponse>('/api/clicks/getcounters', { runId }),

    // ── insight ──
    getNetworkInsight: (runId: number | undefined, allRuns = false) =>
      get<NetworkInsightResponse>('/api/networks/getinsight', { runId, allRuns: allRuns ? true : undefined }),
    getModels: () => get<ModelInfo[]>('/api/models/getall'),
    compareModels: (runId: number | undefined) => get<CompareResponse>('/api/evaluation/compare', { runId }),

    // ── health ──
    getHealth: () => get<HealthStatus>('/api/health/getstatus'),
    dismissLoopEvent: (eventId: number) => post<void>('/api/loop-events/dismiss', { eventId }),

    /** Ends the cookie session on the host, then shows the login page. */
    signOut: async () => {
      try {
        await fetchImpl(LOGOUT_PATH, { method: 'POST', credentials: 'same-origin' })
      } finally {
        window.location.assign(LOGIN_PATH)
      }
    },
  }
}

export type OperatorApi = ReturnType<typeof createOperatorApi>

/** The app's instance. Views import this; tests build their own with a stubbed fetch. */
export const operatorApi: OperatorApi = createOperatorApi()
