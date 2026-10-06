import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, shouldStartInDemo } from '../api/client'
import type { DeliveryDetails, Page, DeliverySummary, WebhookEndpoint, StatsOverview } from '../api/types'
import { DemoEngine, MAX_ATTEMPTS, RETRY_TIERS_SECONDS } from './engine'

function createEngine(random = () => 0.99) {
  let now = Date.parse('2026-10-05T12:00:00Z')
  const engine = new DemoEngine({ now: () => now, random, autoTraffic: false })
  return { engine, advance: (seconds: number) => (now += seconds * 1000) }
}

const get = <T,>(engine: DemoEngine, path: string) => engine.handle('GET', path, undefined, {}) as T

describe('DemoEngine', () => {
  afterEach(() => vi.restoreAllMocks())

  it('faz fan-out apenas para os endpoints cujo padrao casa com o evento', () => {
    const { engine } = createEngine()
    const endpoints = get<WebhookEndpoint[]>(engine, '/v1/endpoints')

    const { id } = engine.ingest('order.shipped', { id: 1 }, null)
    const deliveries = get<Page<DeliverySummary>>(engine, `/v1/deliveries?eventId=${id}`).items

    // order.* (ERP) e # (data lake) casam; invoice.# + order.paid (financeiro) nao.
    const expected = endpoints.filter((e) => e.eventTypes.includes('order.*') || e.eventTypes.includes('#')).map((e) => e.id)
    expect(deliveries.map((d) => d.endpointId).sort()).toEqual(expected.sort())
  })

  it('reagenda com os mesmos degraus do backend e mata depois da ultima tentativa', () => {
    const { engine, advance } = createEngine(() => 0) // tudo falha
    const { id } = engine.ingest('invoice.paid', {}, null)
    const delivery = get<Page<DeliverySummary>>(engine, `/v1/deliveries?eventId=${id}`).items[0]!

    engine.tick()
    for (const seconds of RETRY_TIERS_SECONDS) {
      const current = get<DeliveryDetails>(engine, `/v1/deliveries/${delivery.id}`).delivery
      expect(current.status).toBe('scheduled')
      expect(Date.parse(current.nextAttemptAt!) - Date.parse(current.completedAt ?? current.createdAt)).toBeGreaterThan(0)
      advance(seconds)
      engine.tick()
    }

    const final = get<DeliveryDetails>(engine, `/v1/deliveries/${delivery.id}`)
    expect(final.delivery.status).toBe('dead')
    expect(final.attempts).toHaveLength(MAX_ATTEMPTS)
  })

  it('replay reabre a entrega morta e ela volta a ser tentada', () => {
    const { engine, advance } = createEngine(() => 0)
    const { id } = engine.ingest('invoice.paid', {}, null)
    const deliveryId = get<Page<DeliverySummary>>(engine, `/v1/deliveries?eventId=${id}`).items[0]!.id
    engine.tick()
    for (const seconds of RETRY_TIERS_SECONDS) {
      advance(seconds)
      engine.tick()
    }

    engine.handle('POST', `/v1/deliveries/${deliveryId}/replay`, undefined, {})
    expect(get<DeliveryDetails>(engine, `/v1/deliveries/${deliveryId}`).delivery.status).toBe('pending')

    engine.tick()
    expect(get<DeliveryDetails>(engine, `/v1/deliveries/${deliveryId}`).attempts).toHaveLength(MAX_ATTEMPTS + 1)
  })

  it('idempotency-key devolve o mesmo evento', () => {
    const { engine } = createEngine()
    const first = engine.handle('POST', '/v1/events', { eventType: 'order.paid', payload: { a: 1 } }, { 'Idempotency-Key': 'k1' }) as { id: string }
    const second = engine.handle('POST', '/v1/events', { eventType: 'order.paid', payload: { a: 1 } }, { 'Idempotency-Key': 'k1' }) as { id: string }
    expect(second.id).toBe(first.id)
  })

  it('valida entradas como a API real (Problem Details)', () => {
    const { engine } = createEngine()
    expect(() => engine.handle('POST', '/v1/events', { eventType: 'Pedido Pago', payload: {} }, {})).toThrow(ApiError)
    expect(() => engine.handle('POST', '/v1/endpoints', { url: 'http://x.com', eventTypes: ['#'] }, {})).toThrow(/HTTPS/)
  })

  it('pagina por cursor do mais novo para o mais antigo', () => {
    const { engine } = createEngine()
    const page1 = get<Page<{ sequence: number }>>(engine, '/v1/events?limit=5')
    const page2 = get<Page<{ sequence: number }>>(engine, `/v1/events?limit=5&cursor=${page1.nextCursor}`)
    expect(page1.items).toHaveLength(5)
    expect(page2.items[0]!.sequence).toBeLessThan(page1.items.at(-1)!.sequence)
  })

  it('estatisticas trazem 24 pontos e emitem tentativas para o feed ao vivo', () => {
    const { engine } = createEngine()
    const listener = vi.fn()
    engine.subscribe(listener)
    engine.ingest('order.paid', {}, null)
    engine.tick()

    expect(listener).toHaveBeenCalled()
    expect(get<StatsOverview>(engine, '/v1/stats/overview?hours=24').series).toHaveLength(24)
  })
})

describe('shouldStartInDemo', () => {
  it('abre direto na demo quando publicado sem backend configurado', () => {
    expect(shouldStartInDemo('ponte.vercel.app')).toBe(true)
    expect(shouldStartInDemo('localhost')).toBe(false)
  })
})
