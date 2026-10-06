// Cliente HTTP minimo: toda chamada passa pelo Gateway com a API key do tenant.

const SETTINGS_KEY = 'ponte.console.settings'

export interface ConsoleSettings {
  apiUrl: string
  apiKey: string
  /** Modo demonstracao: backend simulado no navegador (ex.: deploy estatico na Vercel). */
  demo?: boolean
}

export const DEMO_API_KEY = 'pk_test_ponte_demo_key_0000000000000000'

const configuredApiUrl = import.meta.env.VITE_API_URL as string | undefined
const defaultApiUrl = configuredApiUrl ?? 'http://localhost:5100'

export const DEMO_SETTINGS: ConsoleSettings = { apiUrl: 'demo', apiKey: 'demo', demo: true }

/**
 * Publicado sem backend (nenhum VITE_API_URL e fora de localhost): abre direto na
 * demonstracao, em vez de uma tela de conexao que o visitante nao tem como usar.
 */
export function shouldStartInDemo(hostname: string = globalThis.location?.hostname ?? 'localhost'): boolean {
  return !configuredApiUrl && !['localhost', '127.0.0.1', '[::1]'].includes(hostname)
}

export function loadSettings(): ConsoleSettings | null {
  try {
    const raw = localStorage.getItem(SETTINGS_KEY)
    if (raw) {
      const parsed = JSON.parse(raw) as Partial<ConsoleSettings>
      if (parsed.demo) return DEMO_SETTINGS
      // Escolha explicita salva (inclusive "saiu da demo"): respeita e nao reabre sozinho.
      return parsed.apiKey ? { apiUrl: parsed.apiUrl || defaultApiUrl, apiKey: parsed.apiKey } : null
    }
  } catch {
    // storage indisponivel: segue o fluxo padrao
  }
  return shouldStartInDemo() ? DEMO_SETTINGS : null
}

export function saveSettings(settings: ConsoleSettings): void {
  try {
    localStorage.setItem(SETTINGS_KEY, JSON.stringify(settings))
  } catch {
    // modo privado / storage bloqueado: segue so em memoria
  }
}

export function clearSettings(): void {
  try {
    // Marca "saiu" explicitamente para nao reabrir a demo sozinho no proximo acesso.
    localStorage.setItem(SETTINGS_KEY, JSON.stringify({ apiKey: '' }))
  } catch {
    // ignora
  }
}

export function defaultSettings(): ConsoleSettings {
  return { apiUrl: defaultApiUrl, apiKey: '' }
}

/** Erro de API no formato Problem Details (RFC 9457). */
export class ApiError extends Error {
  readonly status: number
  readonly code: string

  constructor(status: number, code: string, message: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
  }
}

interface ProblemDetails {
  title?: string
  detail?: string
  code?: string
  status?: number
}

export async function toApiError(response: Response): Promise<ApiError> {
  let problem: ProblemDetails = {}
  try {
    problem = (await response.json()) as ProblemDetails
  } catch {
    // corpo vazio ou nao-JSON
  }
  const code = problem.code ?? problem.title ?? `http_${response.status}`
  const message = problem.detail ?? problem.title ?? response.statusText ?? 'Erro inesperado'
  return new ApiError(response.status, code, message)
}

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE'
  body?: unknown
  headers?: Record<string, string>
  signal?: AbortSignal
}

export function createApi(settings: ConsoleSettings, fetchImpl: typeof fetch = fetch) {
  const base = settings.apiUrl.replace(/\/+$/, '')

  return async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
    const headers: Record<string, string> = { 'X-Api-Key': settings.apiKey, ...options.headers }
    if (options.body !== undefined) headers['Content-Type'] = 'application/json'

    const response = await fetchImpl(`${base}${path}`, {
      method: options.method ?? 'GET',
      headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      signal: options.signal,
    })

    if (!response.ok) throw await toApiError(response)
    if (response.status === 204) return undefined as T

    const text = await response.text()
    return (text ? JSON.parse(text) : undefined) as T
  }
}

export type Api = ReturnType<typeof createApi>
