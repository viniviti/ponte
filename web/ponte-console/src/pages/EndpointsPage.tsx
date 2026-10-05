import clsx from 'clsx'
import { Eye, Plus, RefreshCw, Trash2 } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { useDeleteEndpoint, useEndpoints, useEndpointSecret, useRotateSecret, useSaveEndpoint } from '../api/hooks'
import type { EndpointSecret, WebhookEndpoint } from '../api/types'
import { PatternInput } from '../components/PatternInput'
import { Button, Card, CopyButton, Drawer, EmptyState, ErrorNotice, Field, PageHeader, Spinner, inputClass } from '../components/ui'
import { formatDuration, formatPercent, successRate } from '../lib/format'

export function EndpointsPage() {
  const endpoints = useEndpoints()
  const [editing, setEditing] = useState<WebhookEndpoint | 'new' | null>(null)

  return (
    <>
      <PageHeader
        title="Endpoints"
        description="URLs dos seus sistemas que recebem webhooks. Cada endpoint tem circuit breaker e limite de concorrência próprios: um servidor lento não atrasa os outros."
        action={
          <Button variant="primary" onClick={() => setEditing('new')}>
            <Plus className="size-4" aria-hidden />
            Novo endpoint
          </Button>
        }
      />

      {endpoints.isLoading ? (
        <Spinner />
      ) : endpoints.error ? (
        <ErrorNotice error={endpoints.error} />
      ) : endpoints.data?.length === 0 ? (
        <Card>
          <EmptyState title="Nenhum endpoint cadastrado">Cadastre a URL que deve receber os eventos e escolha em quais tipos ela se inscreve.</EmptyState>
        </Card>
      ) : (
        <div className="grid gap-4 lg:grid-cols-2">
          {endpoints.data?.map((endpoint) => (
            <EndpointCard key={endpoint.id} endpoint={endpoint} onEdit={() => setEditing(endpoint)} />
          ))}
        </div>
      )}

      <Drawer open={editing !== null} title={editing === 'new' ? 'Novo endpoint' : 'Editar endpoint'} onClose={() => setEditing(null)}>
        {editing !== null && <EndpointForm endpoint={editing === 'new' ? undefined : editing} onDone={() => setEditing(null)} />}
      </Drawer>
    </>
  )
}

function EndpointCard({ endpoint, onEdit }: { endpoint: WebhookEndpoint; onEdit: () => void }) {
  const stats = endpoint.last24h
  const rate = stats ? successRate(stats.succeeded, stats.failed) : null
  const [secret, setSecret] = useState<EndpointSecret | null>(null)
  const reveal = useEndpointSecret()
  const rotate = useRotateSecret()

  return (
    <Card className={clsx(!endpoint.active && 'opacity-70')}>
      <div className="space-y-4 p-5">
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0">
            <p className="font-medium">{endpoint.description || 'Sem descrição'}</p>
            <p className="truncate font-mono text-xs text-muted" title={endpoint.url}>{endpoint.url}</p>
          </div>
          <span className={clsx('shrink-0 rounded-full border px-2 py-0.5 text-xs', endpoint.active ? 'border-success/40 text-success' : 'border-line text-muted')}>
            {endpoint.active ? 'Ativo' : 'Pausado'}
          </span>
        </div>

        <div className="flex flex-wrap gap-1.5">
          {endpoint.eventTypes.map((pattern) => (
            <span key={pattern} className="rounded border border-line bg-surface-2 px-2 py-0.5 font-mono text-xs">{pattern}</span>
          ))}
        </div>

        <div className="grid grid-cols-3 gap-3 rounded-lg border border-line bg-bg/50 p-3 text-center">
          <Metric label="Sucesso 24h" value={rate === null ? '–' : formatPercent(rate)} tone={rate !== null && rate < 0.9 ? 'warning' : undefined} />
          <Metric label="Latência" value={stats ? formatDuration(stats.averageLatencyMs) : '–'} />
          <Metric label="Mortas" value={stats ? String(stats.dead) : '0'} tone={stats && stats.dead > 0 ? 'danger' : undefined} />
        </div>

        {/* Mini barra de saude: proporcao de sucesso nas ultimas 24h */}
        <div className={clsx('h-1.5 overflow-hidden rounded-full', rate === null ? 'bg-line' : 'bg-danger/30')} aria-hidden>
          {rate !== null && <div className="h-full bg-success" style={{ width: `${rate * 100}%` }} />}
        </div>

        {secret && (
          <div className="rounded-md border border-line bg-bg p-3">
            <div className="flex items-center justify-between gap-2">
              <span className="text-xs text-muted">Segredo de assinatura</span>
              <CopyButton value={secret.secret} />
            </div>
            <p className="mt-1 break-all font-mono text-xs">{secret.secret}</p>
            {secret.previousSecretExpiresAt && (
              <p className="mt-2 text-xs text-warning">O segredo anterior continua assinando até {new Date(secret.previousSecretExpiresAt).toLocaleString('pt-BR')}.</p>
            )}
          </div>
        )}
        <ErrorNotice error={reveal.error ?? rotate.error} />

        <div className="flex flex-wrap items-center gap-2 border-t border-line pt-4">
          <Button variant="ghost" onClick={onEdit}>Editar</Button>
          <Button variant="ghost" loading={reveal.isPending} onClick={() => reveal.mutate(endpoint.id, { onSuccess: setSecret })}>
            <Eye className="size-4" aria-hidden />
            Segredo
          </Button>
          <Button variant="ghost" loading={rotate.isPending} onClick={() => rotate.mutate(endpoint.id, { onSuccess: setSecret })}>
            <RefreshCw className="size-4" aria-hidden />
            Rotacionar
          </Button>
          <Link to={`/deliveries?endpointId=${endpoint.id}`} className="ml-auto text-sm text-muted hover:text-accent">Ver entregas</Link>
        </div>
      </div>
    </Card>
  )
}

function Metric({ label, value, tone }: { label: string; value: string; tone?: 'warning' | 'danger' }) {
  return (
    <div>
      <p className={clsx('font-mono text-base tabular', tone === 'warning' && 'text-warning', tone === 'danger' && 'text-danger')}>{value}</p>
      <p className="text-[11px] uppercase tracking-wider text-muted">{label}</p>
    </div>
  )
}

export function EndpointForm({ endpoint, onDone }: { endpoint?: WebhookEndpoint; onDone: () => void }) {
  const save = useSaveEndpoint()
  const remove = useDeleteEndpoint()
  const [url, setUrl] = useState(endpoint?.url ?? 'https://')
  const [description, setDescription] = useState(endpoint?.description ?? '')
  const [eventTypes, setEventTypes] = useState<string[]>(endpoint?.eventTypes ?? [])
  const [maxConcurrency, setMaxConcurrency] = useState(endpoint?.maxConcurrency ?? 10)
  const [active, setActive] = useState(endpoint?.active ?? true)
  const [touched, setTouched] = useState(false)

  const patternsError = touched && eventTypes.length === 0 ? 'Inscreva o endpoint em ao menos um padrão.' : null

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    setTouched(true)
    if (eventTypes.length === 0) return
    try {
      await save.mutateAsync({ id: endpoint?.id, input: { url, description, eventTypes, maxConcurrency, active } })
      onDone()
    } catch {
      // erro exibido abaixo (save.error)
    }
  }

  return (
    <form onSubmit={onSubmit} className="space-y-4">
      <Field label="URL" hint="HTTPS obrigatório em produção. IPs de rede privada são bloqueados (proteção SSRF).">
        <input className={`${inputClass} font-mono`} value={url} onChange={(e) => setUrl(e.target.value)} required aria-label="URL" />
      </Field>
      <Field label="Descrição">
        <input className={inputClass} value={description} onChange={(e) => setDescription(e.target.value)} placeholder="ERP de pedidos" />
      </Field>
      <Field label="Tipos de evento" error={patternsError} hint={<><code>*</code> casa um segmento, <code>#</code> casa vários. Ex.: <code>order.*</code>, <code>invoice.#</code></>}>
        <PatternInput value={eventTypes} onChange={setEventTypes} />
      </Field>
      <div className="grid grid-cols-2 gap-3">
        <Field label="Concorrência máxima" hint="Entregas simultâneas (bulkhead)">
          <input type="number" min={1} max={100} className={inputClass} value={maxConcurrency} onChange={(e) => setMaxConcurrency(Number(e.target.value))} />
        </Field>
        <Field label="Status">
          <label className="flex h-[38px] items-center gap-2 text-sm">
            <input type="checkbox" checked={active} onChange={(e) => setActive(e.target.checked)} className="size-4 accent-[var(--accent)]" />
            Recebendo eventos
          </label>
        </Field>
      </div>

      <ErrorNotice error={save.error ?? remove.error} />

      <div className="flex items-center gap-2 pt-2">
        <Button type="submit" variant="primary" loading={save.isPending}>{endpoint ? 'Salvar' : 'Criar endpoint'}</Button>
        <Button type="button" variant="ghost" onClick={onDone}>Cancelar</Button>
        {endpoint && (
          <Button
            type="button"
            variant="danger"
            className="ml-auto"
            loading={remove.isPending}
            onClick={() => remove.mutate(endpoint.id, { onSuccess: onDone })}
          >
            <Trash2 className="size-4" aria-hidden />
            Remover
          </Button>
        )}
      </div>
    </form>
  )
}
