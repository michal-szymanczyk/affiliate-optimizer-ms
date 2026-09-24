/**
 * The server's error taxonomy, as the client sees it.
 *
 * Codes are part of the published contract and are stable, so the client branches on `code` and
 * never on the message. The message is for a human reading the screen.
 */

/** Codes the server documents (openapi.json). */
export type ApiErrorCode =
  | 'MERCHANT_NOT_SUPPORTED'
  | 'CLICK_EXPIRED'
  | 'CLICK_NOT_FOUND'
  | 'NETWORK_NOT_FOUND'
  | 'INVALID_CURSOR'
  | 'INVALID_REQUEST'
  | 'RATE_LIMITED'
  | 'UNAUTHORIZED'

export type RoutingRejectionCode = Extract<
  ApiErrorCode,
  'MERCHANT_NOT_SUPPORTED' | 'CLICK_EXPIRED'
>

const ROUTING_REJECTION_CODES: readonly RoutingRejectionCode[] = [
  'MERCHANT_NOT_SUPPORTED',
  'CLICK_EXPIRED',
]

/** Only these responses mean the server committed a route that spent the click. */
export function isRoutingRejectionCode(code: string): code is RoutingRejectionCode {
  return (ROUTING_REJECTION_CODES as readonly string[]).includes(code)
}

export class ApiError extends Error {
  readonly status: number
  readonly code: string
  /** Present on RATE_LIMITED: seconds until a token exists. */
  readonly retryAfterSeconds?: number

  constructor(status: number, code: string, message: string, retryAfterSeconds?: number) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    if (retryAfterSeconds !== undefined) this.retryAfterSeconds = retryAfterSeconds
  }

  /**
   * Whether retrying the same call could succeed.
   *
   * RATE_LIMITED is the only documented error that asks the caller to repeat the same request, and
   * only after its stated wait. A false value does not by itself mean a route was committed: 400,
   * 401, and 404 responses also are not retryable but must not be recorded as routing rejections.
   */
  get retryable(): boolean {
    return this.code === 'RATE_LIMITED'
  }
}

/** A transport failure. The server may have completed the request before the response was lost. */
export class TransportError extends Error {
  constructor(url: string, cause: unknown) {
    super(
      `Could not reach the Affiliate Service at ${url}. Check SERVER_URL in .env and that the ` +
        `server is running. (${String(cause)})`,
    )
    this.name = 'TransportError'
  }
}
