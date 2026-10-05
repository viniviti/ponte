import { describe, expect, it, vi } from 'vitest'
import { ApiError, createApi, loadSettings, saveSettings } from './client'

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

describe('createApi', () => {
  const settings = { apiUrl: 'http://gateway:5100/', apiKey: 'pk_test_abc' }

  it('envia a API key e serializa o corpo como JSON', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(202, { id: '1' }))
    const api = createApi(settings, fetchMock)

    const result = await api<{ id: string }>('/v1/events', { method: 'POST', body: { eventType: 'order.paid' } })

    expect(result.id).toBe('1')
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('http://gateway:5100/v1/events')
    expect(init.headers).toMatchObject({ 'X-Api-Key': 'pk_test_abc', 'Content-Type': 'application/json' })
    expect(init.body).toBe('{"eventType":"order.paid"}')
  })

  it('converte Problem Details em ApiError com codigo e mensagem', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse(422, { title: 'event_type_invalid', detail: 'eventType deve seguir o formato', code: 'event_type_invalid', status: 422 }),
    )
    const api = createApi(settings, fetchMock)

    const error = await api('/v1/events').catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({ status: 422, code: 'event_type_invalid', message: 'eventType deve seguir o formato' })
  })

  it('retorna undefined para 204 No Content', async () => {
    const api = createApi(settings, vi.fn().mockResolvedValue(new Response(null, { status: 204 })))
    await expect(api('/v1/endpoints/1', { method: 'DELETE' })).resolves.toBeUndefined()
  })
})

describe('settings', () => {
  it('persiste e recupera a conexao do console', () => {
    expect(loadSettings()).toBeNull()
    saveSettings({ apiUrl: 'http://x', apiKey: 'pk_test_1' })
    expect(loadSettings()).toEqual({ apiUrl: 'http://x', apiKey: 'pk_test_1' })
  })
})
