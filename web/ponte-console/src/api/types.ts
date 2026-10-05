// Contratos espelhados das APIs .NET (JSON em camelCase).

export interface Page<T> {
  items: T[]
  nextCursor: string | null
}

export interface Tenant {
  id: string
  name: string
  plan: string
}

export interface EndpointStats {
  endpointId: string
  succeeded: number
  failed: number
  dead: number
  averageLatencyMs: number
}

export interface WebhookEndpoint {
  id: string
  url: string
  description: string | null
  eventTypes: string[]
  active: boolean
  maxConcurrency: number
  version: number
  secretRotationInProgress: boolean
  createdAt: string
  updatedAt: string
  last24h: EndpointStats | null
}

export interface EndpointInput {
  url: string
  description?: string | null
  eventTypes: string[]
  maxConcurrency?: number
  active?: boolean
}

export interface EndpointSecret {
  secret: string
  previousSecretExpiresAt: string | null
}

export interface EventSummary {
  id: string
  sequence: number
  eventType: string
  receivedAt: string
}

export interface EventDetails {
  id: string
  eventType: string
  payload: unknown
  idempotencyKey: string | null
  receivedAt: string
}

export type DeliveryStatus = 'pending' | 'inFlight' | 'scheduled' | 'succeeded' | 'dead' | 'cancelled'

export interface DeliverySummary {
  id: string
  sequence: number
  eventId: string
  endpointId: string
  eventType: string
  status: DeliveryStatus
  attemptCount: number
  lastStatusCode: number | null
  nextAttemptAt: string | null
  createdAt: string
  completedAt: string | null
}

export interface AttemptView {
  attemptNumber: number
  statusCode: number | null
  succeeded: boolean
  durationMs: number
  error: string | null
  responseSnippet: string | null
  attemptedAt: string
}

export interface DeliveryDetails {
  delivery: DeliverySummary
  body: string
  attempts: AttemptView[]
}

export interface HourlyPoint {
  hour: string
  succeeded: number
  failed: number
  dead: number
}

export interface StatsOverview {
  hours: number
  attempts: number
  succeeded: number
  failed: number
  dead: number
  successRate: number
  averageLatencyMs: number
  series: HourlyPoint[]
}

export interface ApiKeyView {
  id: string
  name: string
  displayPrefix: string
  createdAt: string
  revokedAt: string | null
}

export interface CreatedApiKey {
  id: string
  name: string
  key: string
  displayPrefix: string
  createdAt: string
}

export type LiveOutcome = 'succeeded' | 'retrying' | 'dead'

export interface LiveDeliveryAttempt {
  deliveryId: string
  eventId: string
  endpointId: string
  eventType: string
  attemptNumber: number
  outcome: LiveOutcome
  statusCode: number | null
  durationMs: number
  attemptedAt: string
  nextAttemptAt: string | null
}
