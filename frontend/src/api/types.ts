/**
 * The operator API contract, hand-written.
 *
 * Every shape here mirrors a read model in `backend/AffiliateOptimizer.Domain/Features/**`. They are
 * written by hand rather than generated because the .NET host is in this repository: a drift is
 * caught by the e2e suite and fixed on both sides in one commit.
 *
 * Conventions: JSON is camelCase, enums are camelCase strings, timestamps are ISO 8601 UTC strings,
 * money is a number with four significant decimals. Every response carries `serverNow` -- the ONLY
 * clock the UI derives anything from. The browser's own clock is never consulted.
 */

/** An ISO 8601 timestamp, UTC. */
export type IsoTimestamp = string

// ─── Paging ────────────────────────────────────────────────────────────────────────────────────

export type SortDirection = 'asc' | 'desc'

export interface ListParams {
  /** Zero-based. */
  pageIndex?: number
  /** Default 40 on the server, max 500. */
  pageSize?: number
  /** Property name of the item, case-insensitive. */
  sortBy?: string
  sortDirection?: SortDirection
}

export interface Page<T> {
  items: T[]
  pageIndex: number
  pageSize: number
  totalItems: number
}

// ─── Runs ──────────────────────────────────────────────────────────────────────────────────────

export type RunStatus = 'running' | 'paused' | 'stopped'

export interface ActiveRun {
  runId: number
  status: RunStatus
  modelNames: string[]
  interleaved: boolean
  startedAt: IsoTimestamp
  pausedAt: IsoTimestamp | null
  evidenceWindowMinutes: number
  credentialFingerprint: string
  appVersion: string
}

export interface CurrentRunResponse {
  run: ActiveRun | null
  serverNow: IsoTimestamp
}

export interface RunSummary {
  id: number
  status: RunStatus
  startedAt: IsoTimestamp
  stoppedAt: IsoTimestamp | null
  modelNames: string[]
  interleaved: boolean
  evidenceWindowMinutes: number
  credentialFingerprint: string
  appVersion: string
  clicks: number
  settled: number
  totalCommission: number
  perSettledClick: number | null
}

export interface RunsResponse {
  page: Page<RunSummary>
  serverNow: IsoTimestamp
}

export interface StartRunRequest {
  modelNames: string[]
  interleaved: boolean
  /** Null means the server default (10 minutes). */
  evidenceWindowMinutes: number | null
}

export interface StartRunResponse {
  runId: number
}

// ─── Clicks ────────────────────────────────────────────────────────────────────────────────────

export type ClickState = 'issued' | 'routed' | 'settled' | 'rejected' | 'expired'

export const CLICK_STATES: readonly ClickState[] = ['issued', 'routed', 'settled', 'rejected', 'expired']

export interface ClickRow {
  clickId: string
  runId: number
  ordinal: number
  merchantId: number
  merchantName: string
  issuedAt: IsoTimestamp
  expiresAt: IsoTimestamp
  modelName: string | null
  decidedNetworkId: string | null
  rationale: string | null
  /** A JSON array of `NetworkScore`, or null when no decision was recorded. */
  scoresJson: string | null
  routedAt: IsoTimestamp | null
  routedNetworkId: string | null
  routedNetworkName: string | null
  commission: number | null
  disclosedAt: IsoTimestamp | null
  errorCode: string | null
  /** The route response was lost; the commission feed will reconcile the row. */
  routeUnconfirmed: boolean
  state: ClickState
}

/** One entry of `ClickRow.scoresJson`: what the model estimated for one eligible network. */
export interface NetworkScore {
  networkId: string
  estimate: number
  basis: string
  samples: number
}

export interface ClickFilter {
  runId?: number
  networkId?: string
  state?: ClickState
  /** Name substring, or an exact merchant id. */
  merchant?: string
  /** Rejected and expired only. */
  errorsOnly?: boolean
  modelName?: string
}

/** The columns `GET /api/clicks/getall` accepts in `sortBy`. */
export const CLICK_SORT_KEYS = [
  'clickId',
  'issuedAt',
  'merchantName',
  'routedNetworkName',
  'state',
  'commission',
  'modelName',
] as const

export interface ClicksResponse {
  page: Page<ClickRow>
  serverNow: IsoTimestamp
}

export interface CountersResponse {
  processed: number
  settled: number
  rejected: number
  expired: number
  awaitingDisclosure: number
  completed: number
  totalCommission: number
  perSettledClick: number | null
  /** Disclosures for clicks this store never issued (history replayed by the feed). */
  orphanDisclosures: number
  startedAt: IsoTimestamp | null
  serverNow: IsoTimestamp
}

// ─── Networks ──────────────────────────────────────────────────────────────────────────────────

export interface NetworkInsight {
  networkId: string
  name: string
  advertisedRate: number
  coverageCount: number
  routed: number
  settled: number
  pending: number
  rejected: number
  mean: number | null
  standardError: number | null
  ciLow: number | null
  ciHigh: number | null
  recentSettled: number
  recentMean: number | null
  lastDisclosedAt: IsoTimestamp | null
  claimGap: number | null
  claimGapPercent: number | null
  trend: number | null
  /** Fewer than 10 settled clicks. */
  thin: boolean
  /** No disclosure in the last 5 minutes. */
  stale: boolean
  /** The recent mean differs from the all-time mean beyond 1.96 standard errors. */
  changing: boolean
}

export interface NetworkInsightResponse {
  networks: NetworkInsight[]
  runId: number | null
  recentWindowMinutes: number
  serverNow: IsoTimestamp
}

// ─── Models ────────────────────────────────────────────────────────────────────────────────────

export interface ModelInfo {
  name: string
  version: string
  description: string
  parametersJson: string
}

// ─── Evaluation ────────────────────────────────────────────────────────────────────────────────

export type Verdict = 'inconclusive' | 'noSeparation' | 'leading'

export interface Arm {
  modelName: string
  decided: number
  routed: number
  settled: number
  rejected: number
  expired: number
  pending: number
  totalCommission: number
  mean: number | null
  standardError: number | null
  ciLow: number | null
  ciHigh: number | null
  meanAdvertisedOfChosen: number | null
  claimGap: number | null
  firstHalfMean: number | null
  secondHalfMean: number | null
  conditionsChanged: boolean
}

export interface CompareResponse {
  runId: number | null
  interleaved: boolean
  startedAt: IsoTimestamp | null
  stoppedAt: IsoTimestamp | null
  arms: Arm[]
  verdict: Verdict
  leadingModel: string | null
  caveats: string[]
  minSamplesPerArm: number
  serverNow: IsoTimestamp
}

// ─── Health ────────────────────────────────────────────────────────────────────────────────────

export type Diagnosis =
  | 'healthy'
  | 'noActiveRun'
  | 'paused'
  | 'unauthorized'
  | 'appNotTicking'
  | 'trafficStalled'
  | 'feedStalled'
  | 'modelFaulting'

export type LoopKind = 'click' | 'poll'

export type LoopEventKind = 'api' | 'transport' | 'unconfirmed' | 'clientError' | 'modelError' | 'info'

export interface OpenEvent {
  id: number
  runId: number | null
  loop: LoopKind
  kind: LoopEventKind
  code: string
  message: string
  clickId: string | null
  occurredAt: IsoTimestamp
  retryAt: IsoTimestamp | null
  /** UNAUTHORIZED is never dismissible: nothing works until the credentials are fixed. */
  dismissible: boolean
}

export interface LoopBeat {
  at: IsoTimestamp
  outcome: string
  ageSeconds: number
}

export interface RecentCounts {
  rateLimited: number
  transport: number
  unconfirmed: number
  modelErrors: number
  rejections: number
  clientErrors: number
}

export interface HealthStatus {
  diagnosis: Diagnosis
  summary: string
  runId: number | null
  runStatus: RunStatus | null
  runStartedAt: IsoTimestamp | null
  lastClickIssuedAt: IsoTimestamp | null
  lastRouteAt: IsoTimestamp | null
  lastDisclosureAt: IsoTimestamp | null
  lastPollAt: IsoTimestamp | null
  lastPollSuccessAt: IsoTimestamp | null
  hasCursor: boolean
  clickConsecutiveFailures: number
  pollConsecutiveFailures: number
  awaitingDisclosure: number
  overduePending: number
  orphanDisclosures: number
  recent: RecentCounts
  clickLoop: LoopBeat | null
  pollLoop: LoopBeat | null
  openEvents: OpenEvent[]
  serverNow: IsoTimestamp
}

// ─── Errors ────────────────────────────────────────────────────────────────────────────────────

/** One entry of a 400 validation response. */
export interface ValidationDetail {
  fieldName: string
  errorType: string
  message: string
}
