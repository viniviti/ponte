import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react'
import { getDemoEngine } from '../demo/engine'
import { clearSettings, createApi, loadSettings, saveSettings, type Api, type ConsoleSettings } from './client'

interface SessionValue {
  settings: ConsoleSettings | null
  api: Api | null
  connect: (settings: ConsoleSettings) => void
  disconnect: () => void
}

const SessionContext = createContext<SessionValue | null>(null)

export function SessionProvider({ children, initial }: { children: ReactNode; initial?: ConsoleSettings | null }) {
  const [settings, setSettings] = useState<ConsoleSettings | null>(() => (initial === undefined ? loadSettings() : initial))

  const connect = useCallback((next: ConsoleSettings) => {
    saveSettings(next)
    setSettings(next)
  }, [])

  const disconnect = useCallback(() => {
    clearSettings()
    setSettings(null)
  }, [])

  const value = useMemo<SessionValue>(
    () => ({ settings, api: settings ? apiFor(settings) : null, connect, disconnect }),
    [settings, connect, disconnect],
  )

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>
}

function apiFor(settings: ConsoleSettings): Api {
  if (!settings.demo) return createApi(settings)
  const engine = getDemoEngine()
  return (path, options) => engine.request(path, options)
}

export function useSession(): SessionValue {
  const value = useContext(SessionContext)
  if (!value) throw new Error('useSession precisa estar dentro de <SessionProvider>')
  return value
}

/** API autenticada. So use em telas protegidas (dentro do layout). */
export function useApi(): Api {
  const { api } = useSession()
  if (!api) throw new Error('Sessao nao conectada')
  return api
}
