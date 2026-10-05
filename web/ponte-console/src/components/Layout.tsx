import clsx from 'clsx'
import { Activity, KeyRound, LayoutDashboard, LogOut, Moon, Send, Sun, Webhook } from 'lucide-react'
import { useEffect, useState } from 'react'
import { NavLink, Outlet, useOutletContext } from 'react-router-dom'
import { useTenant } from '../api/hooks'
import { useSession } from '../api/session'
import type { LiveDeliveryAttempt } from '../api/types'
import { useLiveDeliveries, type LiveState } from '../hooks/useLiveDeliveries'

const nav = [
  { to: '/', label: 'Visão geral', icon: LayoutDashboard, end: true },
  { to: '/endpoints', label: 'Endpoints', icon: Webhook },
  { to: '/events', label: 'Eventos', icon: Send },
  { to: '/deliveries', label: 'Entregas', icon: Activity },
  { to: '/api-keys', label: 'Chaves de API', icon: KeyRound },
]

export interface LiveContext {
  feed: LiveDeliveryAttempt[]
  state: LiveState
}

export function useLive(): LiveContext {
  return useOutletContext<LiveContext>()
}

export function Layout() {
  const live = useLiveDeliveries()
  const tenant = useTenant()
  const { disconnect } = useSession()

  return (
    <div className="flex min-h-screen flex-col md:flex-row">
      <aside className="flex shrink-0 flex-col border-b border-line bg-surface/80 backdrop-blur md:sticky md:top-0 md:h-screen md:w-60 md:border-r md:border-b-0">
        <div className="flex items-center gap-2.5 px-5 py-5">
          <BridgeMark />
          <span className="text-lg font-semibold tracking-tight">ponte</span>
          <span className="ml-auto rounded border border-line px-1.5 py-0.5 font-mono text-[10px] uppercase text-muted md:ml-1">console</span>
        </div>

        <nav className="flex gap-1 overflow-x-auto px-3 pb-3 md:flex-col md:overflow-visible md:pb-0" aria-label="Principal">
          {nav.map(({ to, label, icon: Icon, end }) => (
            <NavLink
              key={to}
              to={to}
              end={end}
              className={({ isActive }) =>
                clsx(
                  'flex shrink-0 items-center gap-2.5 rounded-md px-3 py-2 text-sm transition-colors',
                  isActive ? 'bg-surface-2 font-medium text-fg shadow-[inset_2px_0_0_var(--accent)]' : 'text-muted hover:bg-surface-2 hover:text-fg',
                )
              }
            >
              <Icon className="size-4" aria-hidden />
              {label}
            </NavLink>
          ))}
        </nav>

        <div className="mt-auto hidden space-y-3 border-t border-line px-5 py-4 md:block">
          <LiveIndicator state={live.state} />
          <div className="flex items-center justify-between gap-2">
            <div className="min-w-0">
              <p className="truncate text-sm font-medium">{tenant.data?.name ?? '...'}</p>
              <p className="text-xs uppercase tracking-wider text-muted">{tenant.data?.plan ?? ''}</p>
            </div>
            <div className="flex items-center">
              <ThemeToggle />
              <button type="button" onClick={disconnect} className="rounded p-1.5 text-muted hover:bg-surface-2 hover:text-fg" aria-label="Desconectar" title="Desconectar">
                <LogOut className="size-4" />
              </button>
            </div>
          </div>
        </div>
      </aside>

      <main className="min-w-0 flex-1 px-4 py-6 sm:px-8 sm:py-8">
        <div className="mx-auto max-w-6xl">
          <Outlet context={live satisfies LiveContext} />
        </div>
      </main>
    </div>
  )
}

export function LiveIndicator({ state }: { state: LiveState }) {
  const label = { live: 'Ao vivo', connecting: 'Conectando', reconnecting: 'Reconectando', offline: 'Offline' }[state]
  return (
    <span className="inline-flex items-center gap-2 text-xs text-muted">
      <span
        className={clsx(
          'size-2 rounded-full',
          state === 'live' && 'live-dot bg-success',
          (state === 'connecting' || state === 'reconnecting') && 'bg-warning',
          state === 'offline' && 'bg-muted',
        )}
        aria-hidden
      />
      {label}
    </span>
  )
}

function ThemeToggle() {
  const [theme, setTheme] = useState<'light' | 'dark' | null>(() => {
    try {
      return (localStorage.getItem('ponte.theme') as 'light' | 'dark' | null) ?? null
    } catch {
      return null
    }
  })

  useEffect(() => {
    if (theme) document.documentElement.dataset.theme = theme
    else delete document.documentElement.dataset.theme
    try {
      if (theme) localStorage.setItem('ponte.theme', theme)
      else localStorage.removeItem('ponte.theme')
    } catch {
      // ignora
    }
  }, [theme])

  const isDark = theme ? theme === 'dark' : !window.matchMedia?.('(prefers-color-scheme: light)').matches
  return (
    <button
      type="button"
      onClick={() => setTheme(isDark ? 'light' : 'dark')}
      className="rounded p-1.5 text-muted hover:bg-surface-2 hover:text-fg"
      aria-label={isDark ? 'Usar tema claro' : 'Usar tema escuro'}
    >
      {isDark ? <Sun className="size-4" /> : <Moon className="size-4" />}
    </button>
  )
}

export function BridgeMark({ className = 'size-7' }: { className?: string }) {
  return (
    <svg viewBox="0 0 32 32" className={className} aria-hidden>
      <path d="M5 22c4-9 18-9 22 0" fill="none" stroke="var(--accent)" strokeWidth="2.5" strokeLinecap="round" />
      <path d="M4 22h24M10 22v-4.5M16 22v-6.5M22 22v-4.5" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
    </svg>
  )
}
