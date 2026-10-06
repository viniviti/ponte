// Modo demonstracao: um "backend" simulado inteiro no navegador.
// Reproduz o comportamento real da Ponte (fan-out por padrao de evento, retry nos
// mesmos degraus 5s/30s/2m/10m/1h/6h, entregas mortas, replay e feed ao vivo) para
// que o console possa ser explorado sem subir .NET, RabbitMQ e bancos.

import { ApiError, type RequestOptions } from '../api/client'
import type {
  ApiKeyView,
  AttemptView,
  DeliveryStatus,
  DeliverySummary,
  EndpointStats,
  EventSummary,
  HourlyPoint,
  LiveDeliveryAttempt,
  WebhookEndpoint,
} from '../api/types'
import { isValidEventType, isValidPattern, matches } from '../lib/eventPattern'

export const RETRY_TIERS_SECONDS = [5, 30, 120, 600, 3600, 21600]
export const MAX_ATTEMPTS = RETRY_TIERS_SECONDS.length + 1

interface DemoEndpoint extends Omit<WebhookEndpoint, 'last24h'> {
  secret: string
  previousSecretExpiresAt: string | null
  failRate: number
  latencyMs: number
  stats: EndpointStats
}

interface DemoEvent extends EventSummary {
  payload: unknown
  idempotencyKey: string | null
}

interface DemoDelivery extends DeliverySummary {
  body: string
  attempts: AttemptView[]
}

type Listener = (attempt: LiveDeliveryAttempt) => void

export interface EngineOptions {
  now?: () => number
  random?: () => number
  /** Gera eventos sozinho de tempos em tempos (desligado nos testes). */
  autoTraffic?: boolean
}

const samplePayloads: Record<string, () => unknown> = {
  'order.paid': () => ({ orderId: `PED-${randInt(1000, 9999)}`, total: randInt(3000, 90000) / 100, currency: 'BRL', method: pick(['pix', 'card', 'boleto']) }),
  'order.refunded': () => ({ orderId: `PED-${randInt(1000, 9999)}`, amount: randInt(1000, 30000) / 100, reason: 'customer_request' }),
  'order.shipped': () => ({ orderId: `PED-${randInt(1000, 9999)}`, carrier: pick(['Correios', 'Jadlog', 'Loggi']), tracking: `BR${randInt(100000, 999999)}` }),
  'invoice.payment_failed': () => ({ invoiceId: `NF-2026-${randInt(10, 999)}`, attempt: randInt(1, 3), reason: 'card_declined' }),
  'invoice.paid': () => ({ invoiceId: `NF-2026-${randInt(10, 999)}`, amount: randInt(5000, 50000) / 100 }),
  'user.signed_up': () => ({ userId: `usr_${randInt(10000, 99999)}`, plan: pick(['free', 'pro']) }),
}

function randInt(min: number, max: number): number {
  return Math.floor(Math.random() * (max - min + 1)) + min
}

function pick<T>(items: T[]): T {
  return items[Math.floor(Math.random() * items.length)]!
}

function uuid(): string {
  return typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID()
    : 'xxxxxxxx-xxxx-4xxx-8xxx-xxxxxxxxxxxx'.replace(/x/g, () => Math.floor(Math.random() * 16).toString(16))
}

function secret(): string {
  const bytes = Array.from({ length: 32 }, () => Math.floor(Math.random() * 256))
  return 'whsec_' + btoa(String.fromCharCode(...bytes))
}

function hourStart(ms: number): number {
  const d = new Date(ms)
  d.setUTCMinutes(0, 0, 0)
  return d.getTime()
}

export class DemoEngine {
  readonly tenant = { id: '8f14e45f-ceea-4672-a0c2-9f3c5f3e1d01', name: 'Loja Demo', plan: 'pro' }

  private readonly now: () => number
  private readonly random: () => number
  private endpoints: DemoEndpoint[] = []
  private events: DemoEvent[] = []
  private deliveries: DemoDelivery[] = []
  private apiKeys: ApiKeyView[] = []
  private hourly = new Map<number, HourlyPoint & { durationMs: number }>()
  private idempotency = new Map<string, string>()
  private listeners = new Set<Listener>()
  private sequence = 0
  private timers: ReturnType<typeof setInterval>[] = []

  constructor(options: EngineOptions = {}) {
    this.now = options.now ?? Date.now
    this.random = options.random ?? Math.random
    this.seed()
    if (options.autoTraffic ?? true) this.start()
  }

  // ---------------------------------------------------------------------------
  // Simulacao
  // ---------------------------------------------------------------------------

  start(): void {
    if (this.timers.length > 0) return
    this.timers.push(setInterval(() => this.tick(), 500))
    this.timers.push(
      setInterval(() => {
        if (this.random() < 0.7) this.ingest(pick(Object.keys(samplePayloads)), null, null)
      }, 2500),
    )
  }

  stop(): void {
    this.timers.forEach(clearInterval)
    this.timers = []
  }

  subscribe(listener: Listener): () => void {
    this.listeners.add(listener)
    return () => this.listeners.delete(listener)
  }

  /** Processa as entregas vencidas (pendentes ou com retry agendado). */
  tick(): void {
    const now = this.now()
    for (const delivery of this.deliveries) {
      const due =
        delivery.status === 'pending' ||
        (delivery.status === 'scheduled' && delivery.nextAttemptAt !== null && Date.parse(delivery.nextAttemptAt) <= now)
      if (due) this.attempt(delivery, now)
    }
  }

  private attempt(delivery: DemoDelivery, now: number): void {
    const endpoint = this.endpoints.find((e) => e.id === delivery.endpointId)
    if (!endpoint || !endpoint.active) {
      delivery.status = 'cancelled'
      delivery.completedAt = new Date(now).toISOString()
      return
    }

    const failed = this.random() < endpoint.failRate
    const timeout = failed && this.random() < 0.25
    const statusCode = failed ? (timeout ? null : pick([500, 502, 503])) : pick([200, 200, 202, 204])
    const durationMs = timeout ? 10_000 : Math.round(endpoint.latencyMs * (0.6 + this.random() * 0.8))
    const attemptNumber = delivery.attemptCount + 1
    const cycleAttempts = delivery.attempts.filter((a) => Date.parse(a.attemptedAt) >= Date.parse(delivery.createdAt)).length + 1

    delivery.attempts.push({
      attemptNumber,
      statusCode,
      succeeded: !failed,
      durationMs,
      error: timeout ? 'Timeout apos 10s' : null,
      responseSnippet: failed ? (timeout ? null : '{"error":"service unavailable"}') : '{"received":true}',
      attemptedAt: new Date(now).toISOString(),
    })
    delivery.attemptCount = attemptNumber
    delivery.lastStatusCode = statusCode

    let outcome: LiveDeliveryAttempt['outcome']
    if (!failed) {
      delivery.status = 'succeeded'
      delivery.nextAttemptAt = null
      delivery.completedAt = new Date(now).toISOString()
      outcome = 'succeeded'
    } else if (cycleAttempts >= MAX_ATTEMPTS) {
      delivery.status = 'dead'
      delivery.nextAttemptAt = null
      delivery.completedAt = new Date(now).toISOString()
      outcome = 'dead'
    } else {
      delivery.status = 'scheduled'
      delivery.nextAttemptAt = new Date(now + RETRY_TIERS_SECONDS[cycleAttempts - 1]! * 1000).toISOString()
      outcome = 'retrying'
    }

    this.record(endpoint, now, !failed, outcome === 'dead', durationMs)
    const live: LiveDeliveryAttempt = {
      deliveryId: delivery.id,
      eventId: delivery.eventId,
      endpointId: delivery.endpointId,
      eventType: delivery.eventType,
      attemptNumber,
      outcome,
      statusCode,
      durationMs,
      attemptedAt: new Date(now).toISOString(),
      nextAttemptAt: delivery.nextAttemptAt,
    }
    this.listeners.forEach((listener) => listener(live))
  }

  private record(endpoint: DemoEndpoint, at: number, success: boolean, dead: boolean, durationMs: number): void {
    const bucket = hourStart(at)
    const point = this.hourly.get(bucket) ?? { hour: new Date(bucket).toISOString(), succeeded: 0, failed: 0, dead: 0, durationMs: 0 }
    if (success) point.succeeded++
    else point.failed++
    if (dead) point.dead++
    point.durationMs += durationMs
    this.hourly.set(bucket, point)

    const s = endpoint.stats
    const total = s.succeeded + s.failed
    s.averageLatencyMs = Math.round(((s.averageLatencyMs * total + durationMs) / (total + 1)) * 10) / 10
    if (success) s.succeeded++
    else s.failed++
    if (dead) s.dead++
  }

  /** Aceita um evento e faz o fan-out (mesma regra de padroes do backend). */
  ingest(eventType: string, payload: unknown, idempotencyKey: string | null): { id: string; receivedAt: string; replayed: boolean } {
    if (idempotencyKey && this.idempotency.has(idempotencyKey)) {
      const existing = this.events.find((e) => e.id === this.idempotency.get(idempotencyKey))!
      return { id: existing.id, receivedAt: existing.receivedAt, replayed: true }
    }

    const now = this.now()
    const event: DemoEvent = {
      id: uuid(),
      sequence: ++this.sequence,
      eventType,
      receivedAt: new Date(now).toISOString(),
      payload: payload ?? samplePayloads[eventType]?.() ?? {},
      idempotencyKey,
    }
    this.events.unshift(event)
    if (idempotencyKey) this.idempotency.set(idempotencyKey, event.id)

    for (const endpoint of this.endpoints) {
      if (!endpoint.active || !endpoint.eventTypes.some((p) => matches(p, eventType))) continue
      this.deliveries.unshift({
        id: uuid(),
        sequence: ++this.sequence,
        eventId: event.id,
        endpointId: endpoint.id,
        eventType,
        status: 'pending',
        attemptCount: 0,
        lastStatusCode: null,
        nextAttemptAt: null,
        createdAt: event.receivedAt,
        completedAt: null,
        body: JSON.stringify({ type: eventType, timestamp: event.receivedAt, data: event.payload }),
        attempts: [],
      })
    }

    this.trim()
    return { id: event.id, receivedAt: event.receivedAt, replayed: false }
  }

  private trim(): void {
    // Mantem a memoria do navegador sob controle em abas abertas por horas.
    if (this.events.length > 600) this.events.length = 600
    if (this.deliveries.length > 1500) this.deliveries.length = 1500
  }

  // ---------------------------------------------------------------------------
  // Dados iniciais
  // ---------------------------------------------------------------------------

  private seed(): void {
    const now = this.now()
    const created = new Date(now - 30 * 86_400_000).toISOString()
    const endpoint = (url: string, description: string, eventTypes: string[], failRate: number, latencyMs: number, maxConcurrency: number): DemoEndpoint => ({
      id: uuid(),
      url,
      description,
      eventTypes,
      active: true,
      maxConcurrency,
      version: 1,
      secretRotationInProgress: false,
      createdAt: created,
      updatedAt: created,
      secret: secret(),
      previousSecretExpiresAt: null,
      failRate,
      latencyMs,
      stats: { endpointId: '', succeeded: 0, failed: 0, dead: 0, averageLatencyMs: 0 },
    })

    this.endpoints = [
      endpoint('https://erp.lojademo.com.br/webhooks/pedidos', 'ERP de pedidos', ['order.*'], 0.08, 140, 10),
      endpoint('https://legado.financeiro.example/api/hooks', 'Financeiro legado (instável)', ['invoice.#', 'order.paid'], 0.55, 650, 3),
      endpoint('https://ingest.datalake.example/ponte', 'Data lake', ['#'], 0.02, 90, 20),
    ]

    // 24h de historico sintetico para o grafico e os cards.
    for (let h = 23; h >= 1; h--) {
      const at = hourStart(now) - h * 3_600_000
      const wave = Math.sin(((24 - h) / 24) * Math.PI * 2 - 1.2) * 0.5 + 0.5
      for (const ep of this.endpoints) {
        const volume = Math.round((ep.eventTypes.includes('#') ? 260 : 140) * (0.35 + wave))
        const failed = Math.round(volume * ep.failRate * (0.7 + this.random() * 0.6))
        const dead = ep.failRate > 0.3 ? Math.round(failed * 0.03) : 0
        const point = this.hourly.get(at) ?? { hour: new Date(at).toISOString(), succeeded: 0, failed: 0, dead: 0, durationMs: 0 }
        point.succeeded += volume - failed
        point.failed += failed
        point.dead += dead
        point.durationMs += volume * ep.latencyMs
        this.hourly.set(at, point)
        ep.stats.succeeded += volume - failed
        ep.stats.failed += failed
        ep.stats.dead += dead
        ep.stats.averageLatencyMs = ep.latencyMs
      }
    }
    this.endpoints.forEach((ep) => (ep.stats.endpointId = ep.id))

    // Entregas mortas de algumas horas atras (o ciclo completo de retry leva ~7h30),
    // para a tela de DLQ e o replay terem o que mostrar.
    const financeiro = this.endpoints[1]!
    for (let i = 0; i < 5; i++) {
      this.seedEvent(pick(['invoice.payment_failed', 'invoice.paid', 'order.paid']), now - (9 * 3600 + (5 - i) * 600) * 1000, (ep) => ep === financeiro)
    }

    // Trafego recente ja processado.
    for (let i = 0; i < 20; i++) {
      this.seedEvent(pick(Object.keys(samplePayloads)), now - (20 - i) * 95_000, () => false)
    }

        this.apiKeys = [
      { id: uuid(), name: 'Backend de produção', displayPrefix: 'pk_live_7c2e', createdAt: created, revokedAt: null },
      { id: uuid(), name: 'Demo', displayPrefix: 'pk_test_pont', createdAt: created, revokedAt: null },
      { id: uuid(), name: 'CI antigo', displayPrefix: 'pk_live_3fa9', createdAt: created, revokedAt: new Date(now - 86_400_000 * 7).toISOString() },
    ]
  }

  /** Cria um evento no passado com as entregas ja resolvidas (mortas ou entregues). */
  private seedEvent(eventType: string, at: number, dies: (endpoint: DemoEndpoint) => boolean): void {
    const result = this.ingest(eventType, null, null)
    const event = this.events.find((e) => e.id === result.id)!
    event.receivedAt = new Date(at).toISOString()

    for (const delivery of this.deliveries.filter((d) => d.eventId === event.id)) {
      const endpoint = this.endpoints.find((e) => e.id === delivery.endpointId)!
      const willDie = dies(endpoint)
      const attempts = willDie ? MAX_ATTEMPTS : 1 + (this.random() < endpoint.failRate ? 1 : 0)
      delivery.createdAt = event.receivedAt
      delivery.body = JSON.stringify({ type: eventType, timestamp: event.receivedAt, data: event.payload })

      let t = at + 300
      for (let n = 1; n <= attempts; n++) {
        const ok = !willDie && n === attempts
        delivery.attempts.push({
          attemptNumber: n,
          statusCode: ok ? 200 : 503,
          succeeded: ok,
          durationMs: Math.round(endpoint.latencyMs * (0.7 + this.random() * 0.6)),
          error: null,
          responseSnippet: ok ? '{"received":true}' : '{"error":"service unavailable"}',
          attemptedAt: new Date(t).toISOString(),
        })
        t += (RETRY_TIERS_SECONDS[n - 1] ?? 0) * 1000
      }

      delivery.attemptCount = attempts
      delivery.lastStatusCode = willDie ? 503 : 200
      delivery.status = willDie ? 'dead' : 'succeeded'
      delivery.completedAt = delivery.attempts.at(-1)!.attemptedAt
    }
  }

  // ---------------------------------------------------------------------------
  // API simulada (mesmas rotas e formatos do Gateway)
  // ---------------------------------------------------------------------------

  async request<T>(path: string, options: RequestOptions = {}): Promise<T> {
    // Latencia de rede simulada: o console mostra os estados de carregamento.
    await new Promise((resolve) => setTimeout(resolve, 80 + this.random() * 120))
    return this.handle(options.method ?? 'GET', path, options.body, options.headers ?? {}) as T
  }

  handle(method: string, path: string, body: unknown, headers: Record<string, string>): unknown {
    const url = new URL(path, 'http://demo')
    const q = url.searchParams
    const parts = url.pathname.split('/').filter(Boolean) // ["v1", "endpoints", ":id", ...]
    const [, resource, id, action] = parts
    const route = `${method} ${resource ?? ''}${id ? '/:id' : ''}${action ? `/${action}` : ''}`

    switch (route) {
      case 'GET tenant':
        return this.tenant
      case 'GET stats/:id':
        return this.stats(Number(q.get('hours') ?? 24))

      case 'GET endpoints':
        return this.endpoints.map((e) => this.endpointView(e))
      case 'POST endpoints':
        return this.saveEndpoint(undefined, body as Partial<WebhookEndpoint>)
      case 'PUT endpoints/:id':
        return this.saveEndpoint(id, body as Partial<WebhookEndpoint>)
      case 'DELETE endpoints/:id':
        this.endpoints = this.endpoints.filter((e) => e.id !== id)
        return undefined
      case 'GET endpoints/:id/secret': {
        const e = this.findEndpoint(id!)
        return { secret: e.secret, previousSecretExpiresAt: e.previousSecretExpiresAt }
      }
      case 'POST endpoints/:id/rotate-secret': {
        const e = this.findEndpoint(id!)
        e.secret = secret()
        e.previousSecretExpiresAt = new Date(this.now() + 86_400_000).toISOString()
        e.secretRotationInProgress = true
        e.version++
        return { secret: e.secret, previousSecretExpiresAt: e.previousSecretExpiresAt }
      }

      case 'GET events':
        return this.page(
          this.events.filter((e) => !q.get('eventType') || e.eventType === q.get('eventType')),
          q,
          (e) => ({ id: e.id, sequence: e.sequence, eventType: e.eventType, receivedAt: e.receivedAt }),
        )
      case 'GET events/:id': {
        const e = this.events.find((x) => x.id === id)
        if (!e) throw new ApiError(404, 'not_found', 'Evento não encontrado')
        return { id: e.id, eventType: e.eventType, payload: e.payload, idempotencyKey: e.idempotencyKey, receivedAt: e.receivedAt }
      }
      case 'POST events': {
        const input = body as { eventType?: string; payload?: unknown }
        if (!input?.eventType || !isValidEventType(input.eventType)) {
          throw new ApiError(422, 'event_type_invalid', "eventType deve seguir o formato 'dominio.acao' em minúsculas (ex.: order.paid).")
        }
        if (typeof input.payload !== 'object' || input.payload === null) {
          throw new ApiError(422, 'payload_invalid', 'payload deve ser um objeto ou array JSON.')
        }
        const result = this.ingest(input.eventType, input.payload, headers['Idempotency-Key'] ?? null)
        return { id: result.id, receivedAt: result.receivedAt }
      }

      case 'GET deliveries': {
        const status = q.get('status') as DeliveryStatus | null
        return this.page(
          this.deliveries.filter(
            (d) =>
              (!status || d.status === status) &&
              (!q.get('endpointId') || d.endpointId === q.get('endpointId')) &&
              (!q.get('eventId') || d.eventId === q.get('eventId')),
          ),
          q,
          (d) => this.summary(d),
        )
      }
      case 'GET deliveries/:id': {
        const d = this.deliveries.find((x) => x.id === id)
        if (!d) throw new ApiError(404, 'not_found', 'Entrega não encontrada')
        return { delivery: this.summary(d), body: d.body, attempts: [...d.attempts] }
      }
      case 'POST deliveries/:id/replay': {
        const d = this.deliveries.find((x) => x.id === id)
        if (!d) throw new ApiError(404, 'not_found', 'Entrega não encontrada')
        if (d.status !== 'dead' && d.status !== 'cancelled') {
          throw new ApiError(409, 'delivery_not_replayable', 'Somente entregas mortas ou canceladas podem ser reenviadas.')
        }
        this.replay(d)
        return undefined
      }
      case 'POST deliveries/:id': {
        // replay-dead (o segmento "replay-dead" cai na posicao do id)
        if (id !== 'replay-dead') break
        const dead = this.deliveries.filter((d) => d.status === 'dead' && (!q.get('endpointId') || d.endpointId === q.get('endpointId')))
        dead.forEach((d) => this.replay(d))
        return { replayed: dead.length }
      }

      case 'GET api-keys':
        return this.apiKeys
      case 'POST api-keys': {
        const name = (body as { name?: string })?.name?.trim()
        if (!name) throw new ApiError(422, 'name_invalid', 'Informe um nome para a chave.')
        const key = 'pk_live_' + Array.from({ length: 64 }, () => Math.floor(this.random() * 16).toString(16)).join('')
        const view: ApiKeyView = { id: uuid(), name, displayPrefix: key.slice(0, 12), createdAt: new Date(this.now()).toISOString(), revokedAt: null }
        this.apiKeys.unshift(view)
        return { ...view, key }
      }
      case 'DELETE api-keys/:id': {
        const key = this.apiKeys.find((k) => k.id === id)
        if (key) key.revokedAt = new Date(this.now()).toISOString()
        return undefined
      }
    }

    throw new ApiError(404, 'not_found', `Rota ${method} ${url.pathname} não existe na demonstração`)
  }

  private replay(delivery: DemoDelivery): void {
    delivery.status = 'pending'
    delivery.nextAttemptAt = null
    delivery.completedAt = null
    delivery.createdAt = new Date(this.now()).toISOString() // novo ciclo de retry
  }

  private summary(d: DemoDelivery): DeliverySummary {
    const { body: _body, attempts: _attempts, ...summary } = d
    return { ...summary }
  }

  private page<TItem extends { sequence: number }, TOut>(items: TItem[], q: URLSearchParams, map: (item: TItem) => TOut) {
    const limit = Math.min(Math.max(Number(q.get('limit') ?? 50), 1), 200)
    const cursor = Number(q.get('cursor') ?? 0)
    const filtered = cursor > 0 ? items.filter((i) => i.sequence < cursor) : items
    const slice = filtered.slice(0, limit)
    return {
      items: slice.map(map),
      nextCursor: filtered.length > limit ? String(slice.at(-1)!.sequence) : null,
    }
  }

  private stats(hours: number) {
    const span = Math.min(Math.max(hours, 1), 24 * 30)
    const current = hourStart(this.now())
    const series: HourlyPoint[] = []
    let succeeded = 0
    let failed = 0
    let dead = 0
    let duration = 0
    for (let i = span - 1; i >= 0; i--) {
      const bucket = current - i * 3_600_000
      const p = this.hourly.get(bucket)
      series.push({ hour: new Date(bucket).toISOString(), succeeded: p?.succeeded ?? 0, failed: p?.failed ?? 0, dead: p?.dead ?? 0 })
      succeeded += p?.succeeded ?? 0
      failed += p?.failed ?? 0
      dead += p?.dead ?? 0
      duration += p?.durationMs ?? 0
    }
    const attempts = succeeded + failed
    return {
      hours: span,
      attempts,
      succeeded,
      failed,
      dead,
      successRate: attempts === 0 ? 1 : Math.round((succeeded / attempts) * 10_000) / 10_000,
      averageLatencyMs: attempts === 0 ? 0 : Math.round((duration / attempts) * 10) / 10,
      series,
    }
  }

  private findEndpoint(id: string): DemoEndpoint {
    const endpoint = this.endpoints.find((e) => e.id === id)
    if (!endpoint) throw new ApiError(404, 'endpoint_not_found', `Endpoint ${id} não encontrado.`)
    return endpoint
  }

  private endpointView(e: DemoEndpoint): WebhookEndpoint {
    const { secret: _s, previousSecretExpiresAt: _p, failRate: _f, latencyMs: _l, stats, ...view } = e
    return { ...view, eventTypes: [...e.eventTypes], last24h: { ...stats } }
  }

  private saveEndpoint(id: string | undefined, input: Partial<WebhookEndpoint>): WebhookEndpoint {
    const url = input.url ?? ''
    if (!/^https:\/\/[^\s/@]+\.[^\s/@]+/.test(url)) {
      throw new ApiError(422, 'url_invalid', 'O endpoint precisa usar HTTPS e um domínio público.')
    }
    const eventTypes = (input.eventTypes ?? []).map((t) => t.trim().toLowerCase()).filter(Boolean)
    if (eventTypes.length === 0) throw new ApiError(422, 'event_types_required', 'Inscreva o endpoint em ao menos um tipo de evento.')
    const invalid = eventTypes.find((t) => !isValidPattern(t))
    if (invalid) throw new ApiError(422, 'event_type_pattern_invalid', `Padrão inválido: '${invalid}'.`)
    const maxConcurrency = input.maxConcurrency ?? 10
    if (maxConcurrency < 1 || maxConcurrency > 100) throw new ApiError(422, 'max_concurrency_invalid', 'maxConcurrency deve estar entre 1 e 100.')

    const now = new Date(this.now()).toISOString()
    if (id) {
      const e = this.findEndpoint(id)
      Object.assign(e, { url, description: input.description ?? null, eventTypes, maxConcurrency, active: input.active ?? e.active, updatedAt: now })
      e.version++
      return this.endpointView(e)
    }

    const created: DemoEndpoint = {
      id: uuid(),
      url,
      description: input.description ?? null,
      eventTypes,
      active: input.active ?? true,
      maxConcurrency,
      version: 1,
      secretRotationInProgress: false,
      createdAt: now,
      updatedAt: now,
      secret: secret(),
      previousSecretExpiresAt: null,
      failRate: 0.05,
      latencyMs: 120,
      stats: { endpointId: '', succeeded: 0, failed: 0, dead: 0, averageLatencyMs: 0 },
    }
    created.stats.endpointId = created.id
    this.endpoints.push(created)
    return this.endpointView(created)
  }
}

let shared: DemoEngine | null = null

/** Uma unica simulacao por aba: o estado sobrevive a navegacao entre telas. */
export function getDemoEngine(): DemoEngine {
  shared ??= new DemoEngine()
  return shared
}
