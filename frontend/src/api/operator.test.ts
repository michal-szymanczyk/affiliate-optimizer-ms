/**
 * The operator API client, driven against a stubbed fetch.
 *
 * Two things are worth pinning here. The query-string builder must OMIT empty filters rather than
 * send `state=`: the host binds an absent parameter to "no filter" but an empty one to a binding
 * error. And every failure, whatever the host sent, must come out as one OperatorApiError the views
 * can branch on by status and code -- never on message text.
 */
import { describe, expect, it, vi } from 'vitest'
import { buildQuery, createOperatorApi, loginUrl, normalizeError, OperatorApiError } from './operator'

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function stubFetch(...responses: (Response | Error)[]) {
  const queue = [...responses]
  const calls: { url: string; init: RequestInit | undefined }[] = []
  const fetchImpl = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    calls.push({ url: String(input), init })
    const next = queue.shift()
    if (next === undefined) throw new Error('stubFetch: no response queued')
    if (next instanceof Error) throw next
    return next
  }) as unknown as typeof fetch
  return { fetchImpl, calls }
}

describe('buildQuery', () => {
  it('returns an empty string when nothing is set', () => {
    expect(buildQuery({})).toBe('')
    expect(buildQuery({ runId: undefined, state: null, merchant: '' })).toBe('')
  })

  it('omits undefined, null and empty values but keeps zero and false', () => {
    const query = buildQuery({
      pageIndex: 0,
      errorsOnly: false,
      runId: undefined,
      networkId: null,
      merchant: '',
      state: 'settled',
    })
    expect(query).toBe('?pageIndex=0&errorsOnly=false&state=settled')
  })

  it('encodes values that are not URL-safe', () => {
    expect(buildQuery({ merchant: 'Best Buy & co' })).toBe('?merchant=Best+Buy+%26+co')
  })
})

describe('loginUrl', () => {
  it('sends the operator back to where they were, query string included', () => {
    expect(loginUrl('/', '')).toBe('/authentication/login?returnUrl=%2F')
    expect(loginUrl('/', '?tab=networks')).toBe('/authentication/login?returnUrl=%2F%3Ftab%3Dnetworks')
  })
})

describe('normalizeError', () => {
  it('keeps the validation details of a 400', () => {
    const error = normalizeError(400, '/api/runs/start', {
      message: 'Validation failed.',
      details: [{ fieldName: 'ModelNames', errorType: 'NotEmptyValidator', message: 'Choose at least one optimization model.' }],
    })
    expect(error).toBeInstanceOf(OperatorApiError)
    expect(error.status).toBe(400)
    expect(error.message).toBe('Validation failed.')
    expect(error.details).toEqual([
      { fieldName: 'ModelNames', errorType: 'NotEmptyValidator', message: 'Choose at least one optimization model.' },
    ])
  })

  it("carries the Affiliate Service's own code through a 502", () => {
    const error = normalizeError(502, '/api/runs/start', {
      message: 'The Affiliate Service answered 429 RATE_LIMITED: slow down',
      code: 'RATE_LIMITED',
      upstreamStatus: 429,
      retryAfterSeconds: 4,
    })
    expect(error.code).toBe('RATE_LIMITED')
    expect(error.upstreamStatus).toBe(429)
    expect(error.retryAfterSeconds).toBe(4)
    expect(error.isUpstream).toBe(true)
  })

  it('marks a 503 as an upstream transport failure', () => {
    const error = normalizeError(503, '/api/runs/start', { message: 'unreachable', code: 'TRANSPORT' })
    expect(error.code).toBe('TRANSPORT')
    expect(error.isUpstream).toBe(true)
  })

  it('invents a message when the body has none, and ignores malformed details', () => {
    const error = normalizeError(500, '/api/health/getstatus', { details: 'not a list' })
    expect(error.message).toBe('/api/health/getstatus returned 500.')
    expect(error.details).toBeUndefined()
    expect(error.isUpstream).toBe(false)
  })

  it('copes with a body that is not an object at all', () => {
    expect(normalizeError(404, '/api/x', null).status).toBe(404)
    expect(normalizeError(404, '/api/x', 'gone').message).toBe('/api/x returned 404.')
  })
})

describe('createOperatorApi', () => {
  it('sends the cookie, asks for JSON and builds the query string', async () => {
    const stub = stubFetch(jsonResponse(200, { page: { items: [], pageIndex: 0, pageSize: 40, totalItems: 0 }, serverNow: 'x' }))
    const api = createOperatorApi({ fetchImpl: stub.fetchImpl })

    await api.getClicks({ runId: 7, state: 'settled', merchant: '' }, { pageIndex: 0, pageSize: 40 })

    expect(stub.calls[0]?.url).toBe('/api/clicks/getall?runId=7&state=settled&pageIndex=0&pageSize=40')
    expect(stub.calls[0]?.init?.credentials).toBe('same-origin')
    expect((stub.calls[0]?.init?.headers as Record<string, string>).Accept).toBe('application/json')
  })

  it('posts JSON bodies and accepts an empty 200', async () => {
    const stub = stubFetch(new Response('', { status: 200 }))
    const api = createOperatorApi({ fetchImpl: stub.fetchImpl })

    await expect(api.pauseRun(3)).resolves.toBeNull()

    expect(stub.calls[0]?.init?.method).toBe('POST')
    expect(stub.calls[0]?.init?.body).toBe('{"runId":3}')
    expect((stub.calls[0]?.init?.headers as Record<string, string>)['Content-Type']).toBe('application/json')
  })

  it('turns a 412 into a typed error the views can branch on', async () => {
    const stub = stubFetch(jsonResponse(412, { message: 'A run is already active. Stop it before starting another one.' }))
    const api = createOperatorApi({ fetchImpl: stub.fetchImpl })

    await expect(api.startRun({ modelNames: ['random'], interleaved: false, evidenceWindowMinutes: null })).rejects.toMatchObject({
      name: 'OperatorApiError',
      status: 412,
      message: 'A run is already active. Stop it before starting another one.',
    })
  })

  it('redirects to the login page on a 401 and still throws, so callers stop', async () => {
    const onUnauthorized = vi.fn()
    const stub = stubFetch(new Response('', { status: 401 }))
    const api = createOperatorApi({ fetchImpl: stub.fetchImpl, onUnauthorized })

    await expect(api.getHealth()).rejects.toMatchObject({ status: 401, code: 'UNAUTHENTICATED' })
    expect(onUnauthorized).toHaveBeenCalledTimes(1)
  })

  it('reports a host that cannot be reached as status 0 / NETWORK', async () => {
    const stub = stubFetch(new TypeError('Failed to fetch'))
    const api = createOperatorApi({ fetchImpl: stub.fetchImpl })

    await expect(api.getCurrentRun()).rejects.toMatchObject({ status: 0, code: 'NETWORK' })
  })

  it('refuses a 2xx that is not JSON rather than returning garbage', async () => {
    const stub = stubFetch(new Response('<html>login</html>', { status: 200 }))
    const api = createOperatorApi({ fetchImpl: stub.fetchImpl })

    await expect(api.getModels()).rejects.toMatchObject({ status: 200, code: 'UNREADABLE_RESPONSE' })
  })

  it('still reports the status of a non-JSON error page', async () => {
    const stub = stubFetch(new Response('Bad Gateway', { status: 502 }))
    const api = createOperatorApi({ fetchImpl: stub.fetchImpl })

    await expect(api.getModels()).rejects.toMatchObject({ status: 502, message: expect.stringContaining('502') })
  })
})
