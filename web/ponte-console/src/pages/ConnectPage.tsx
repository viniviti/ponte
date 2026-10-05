import { useState, type FormEvent } from 'react'
import { createApi, DEMO_API_KEY, defaultSettings } from '../api/client'
import { useSession } from '../api/session'
import { BridgeMark } from '../components/Layout'
import { Button, ErrorNotice, Field, inputClass } from '../components/ui'

export function ConnectPage() {
  const { connect } = useSession()
  const [apiUrl, setApiUrl] = useState(defaultSettings().apiUrl)
  const [apiKey, setApiKey] = useState('')
  const [error, setError] = useState<unknown>(null)
  const [checking, setChecking] = useState(false)

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    setChecking(true)
    setError(null)
    try {
      // Valida a chave antes de entrar (o Gateway responde 401 se for invalida).
      await createApi({ apiUrl, apiKey: apiKey.trim() })('/v1/tenant')
      connect({ apiUrl, apiKey: apiKey.trim() })
    } catch (err) {
      setError(err instanceof TypeError ? new Error('Gateway inacessível. Ele está rodando?') : err)
    } finally {
      setChecking(false)
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center px-4">
      <form onSubmit={onSubmit} className="w-full max-w-md rounded-2xl border border-line bg-surface p-8 shadow-xl">
        <div className="mb-6 flex items-center gap-3">
          <BridgeMark className="size-10" />
          <div>
            <h1 className="text-xl font-semibold tracking-tight">Ponte Console</h1>
            <p className="text-sm text-muted">Entrega confiável de webhooks</p>
          </div>
        </div>

        <div className="space-y-4">
          <Field label="URL do Gateway">
            <input className={inputClass} value={apiUrl} onChange={(e) => setApiUrl(e.target.value)} required />
          </Field>
          <Field
            label="API key"
            hint={
              <>
                Rodando com docker compose?{' '}
                <button type="button" className="text-accent underline-offset-2 hover:underline" onClick={() => setApiKey(DEMO_API_KEY)}>
                  Usar a chave de demonstração
                </button>
              </>
            }
          >
            <input className={`${inputClass} font-mono`} value={apiKey} onChange={(e) => setApiKey(e.target.value)} placeholder="pk_live_..." required autoComplete="off" />
          </Field>
          <ErrorNotice error={error} />
          <Button type="submit" variant="primary" className="w-full" loading={checking}>
            Conectar
          </Button>
        </div>
      </form>
    </div>
  )
}
