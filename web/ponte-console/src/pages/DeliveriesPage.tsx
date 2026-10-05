import clsx from 'clsx'
import { RotateCcw } from 'lucide-react'
import { useSearchParams } from 'react-router-dom'
import { useDeliveries, useDelivery, useEndpoints, useReplayAllDead, useReplayDelivery } from '../api/hooks'
import type { DeliveryStatus } from '../api/types'
import { Button, Card, Drawer, EmptyState, ErrorNotice, HttpStatus, JsonBlock, PageHeader, Spinner, StatusBadge } from '../components/ui'
import { formatDateTime, formatDuration, formatRelative, shortId } from '../lib/format'

const tabs: { value: DeliveryStatus | ''; label: string }[] = [
  { value: '', label: 'Todas' },
  { value: 'succeeded', label: 'Entregues' },
  { value: 'scheduled', label: 'Reagendadas' },
  { value: 'dead', label: 'Mortas' },
  { value: 'cancelled', label: 'Canceladas' },
]

export function DeliveriesPage() {
  const [params, setParams] = useSearchParams()
  const status = (params.get('status') ?? '') as DeliveryStatus | ''
  const endpointId = params.get('endpointId') ?? undefined
  const selected = params.get('id') ?? undefined

  const deliveries = useDeliveries({ status, endpointId })
  const endpoints = useEndpoints()
  const replayAll = useReplayAllDead()
  const items = deliveries.data?.pages.flatMap((p) => p.items) ?? []
  const endpointUrls = new Map(endpoints.data?.map((e) => [e.id, e.url]))

  function update(next: Record<string, string | undefined>) {
    const merged = new URLSearchParams(params)
    for (const [key, value] of Object.entries(next)) {
      if (value) merged.set(key, value)
      else merged.delete(key)
    }
    setParams(merged)
  }

  return (
    <>
      <PageHeader
        title="Entregas"
        description="Uma entrega é um evento indo para um endpoint. Falhas transitórias são reagendadas com backoff exponencial (5s, 30s, 2m, 10m, 1h, 6h); depois disso a entrega morre e pode ser reenviada."
        action={
          status === 'dead' && items.length > 0 ? (
            <Button variant="danger" loading={replayAll.isPending} onClick={() => replayAll.mutate(endpointId)}>
              <RotateCcw className="size-4" aria-hidden />
              Reenviar todas as mortas
            </Button>
          ) : undefined
        }
      />

      {replayAll.data && (
        <p className="mb-4 rounded-md border border-success/40 bg-success/10 px-3.5 py-2.5 text-sm text-success">
          {replayAll.data.replayed} entrega(s) voltaram para a fila.
        </p>
      )}

      <div className="mb-4 flex flex-wrap items-center gap-2">
        <div className="flex rounded-lg border border-line bg-surface p-1" role="tablist" aria-label="Filtrar por status">
          {tabs.map((tab) => (
            <button
              key={tab.value}
              type="button"
              role="tab"
              aria-selected={status === tab.value}
              onClick={() => update({ status: tab.value || undefined })}
              className={clsx('rounded-md px-3 py-1.5 text-sm', status === tab.value ? 'bg-surface-2 font-medium' : 'text-muted hover:text-fg')}
            >
              {tab.label}
            </button>
          ))}
        </div>
        <select
          className="h-10 rounded-lg border border-line bg-surface px-3 text-sm"
          value={endpointId ?? ''}
          onChange={(e) => update({ endpointId: e.target.value || undefined })}
          aria-label="Filtrar por endpoint"
        >
          <option value="">Todos os endpoints</option>
          {endpoints.data?.map((endpoint) => (
            <option key={endpoint.id} value={endpoint.id}>{endpoint.description || endpoint.url}</option>
          ))}
        </select>
      </div>

      <Card>
        {deliveries.isLoading ? (
          <Spinner />
        ) : items.length === 0 ? (
          <EmptyState title="Nada por aqui">Nenhuma entrega com esse filtro.</EmptyState>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[720px] text-sm">
              <thead className="text-left text-xs uppercase tracking-wider text-muted">
                <tr>
                  <th className="px-5 py-2.5 font-medium">Status</th>
                  <th className="px-5 py-2.5 font-medium">Evento</th>
                  <th className="px-5 py-2.5 font-medium">Endpoint</th>
                  <th className="px-5 py-2.5 text-right font-medium">Tentativas</th>
                  <th className="px-5 py-2.5 text-right font-medium">Último HTTP</th>
                  <th className="px-5 py-2.5 text-right font-medium">Quando</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-line">
                {items.map((delivery) => (
                  <tr key={delivery.id} className="cursor-pointer hover:bg-surface-2" onClick={() => update({ id: delivery.id })}>
                    <td className="px-5 py-3"><StatusBadge status={delivery.status} /></td>
                    <td className="px-5 py-3 font-mono">{delivery.eventType}</td>
                    <td className="max-w-[220px] truncate px-5 py-3 text-muted" title={endpointUrls.get(delivery.endpointId)}>
                      {endpointUrls.get(delivery.endpointId) ?? shortId(delivery.endpointId)}
                    </td>
                    <td className="px-5 py-3 text-right font-mono tabular">{delivery.attemptCount}</td>
                    <td className="px-5 py-3 text-right">{delivery.attemptCount > 0 ? <HttpStatus code={delivery.lastStatusCode} /> : '–'}</td>
                    <td className="px-5 py-3 text-right text-muted">
                      {delivery.status === 'scheduled' && delivery.nextAttemptAt
                        ? `retry ${formatRelative(delivery.nextAttemptAt)}`
                        : formatRelative(delivery.completedAt ?? delivery.createdAt)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {deliveries.hasNextPage && (
          <div className="border-t border-line p-3 text-center">
            <Button variant="ghost" onClick={() => void deliveries.fetchNextPage()} loading={deliveries.isFetchingNextPage}>Carregar mais</Button>
          </div>
        )}
      </Card>

      <DeliveryDrawer id={selected} onClose={() => update({ id: undefined })} />
    </>
  )
}

function DeliveryDrawer({ id, onClose }: { id?: string; onClose: () => void }) {
  const details = useDelivery(id)
  const replay = useReplayDelivery()
  const delivery = details.data?.delivery

  return (
    <Drawer open={Boolean(id)} title={delivery ? `Entrega ${shortId(delivery.id)}` : 'Entrega'} onClose={onClose}>
      {details.isLoading || !details.data || !delivery ? (
        details.error ? <ErrorNotice error={details.error} /> : <Spinner />
      ) : (
        <div className="space-y-6">
          <div className="flex items-center justify-between gap-3">
            <StatusBadge status={delivery.status} />
            {(delivery.status === 'dead' || delivery.status === 'cancelled') && (
              <Button variant="primary" loading={replay.isPending} onClick={() => replay.mutate(delivery.id)}>
                <RotateCcw className="size-4" aria-hidden />
                Reenviar
              </Button>
            )}
          </div>
          <ErrorNotice error={replay.error} />

          <dl className="grid grid-cols-[120px_1fr] gap-y-2 text-sm">
            <dt className="text-muted">Evento</dt>
            <dd className="font-mono text-xs">{delivery.eventType} · {delivery.eventId}</dd>
            <dt className="text-muted">Criada</dt>
            <dd>{formatDateTime(delivery.createdAt)}</dd>
            {delivery.nextAttemptAt && (
              <>
                <dt className="text-muted">Próxima</dt>
                <dd>{formatDateTime(delivery.nextAttemptAt)} ({formatRelative(delivery.nextAttemptAt)})</dd>
              </>
            )}
          </dl>

          <div>
            <h3 className="mb-3 text-sm font-semibold">Tentativas</h3>
            {details.data.attempts.length === 0 ? (
              <p className="text-sm text-muted">Nenhuma tentativa ainda.</p>
            ) : (
              <ol className="relative space-y-4 border-l border-line pl-5">
                {details.data.attempts.map((attempt) => (
                  <li key={attempt.attemptNumber} className="relative">
                    <span
                      className={clsx('absolute -left-[26px] top-1 size-3 rounded-full border-2 border-surface', attempt.succeeded ? 'bg-success' : 'bg-danger')}
                      aria-hidden
                    />
                    <div className="flex items-center gap-3 text-sm">
                      <span className="font-medium">#{attempt.attemptNumber}</span>
                      <HttpStatus code={attempt.statusCode} />
                      <span className="font-mono text-xs text-muted tabular">{formatDuration(attempt.durationMs)}</span>
                      <span className="ml-auto text-xs text-muted">{formatDateTime(attempt.attemptedAt)}</span>
                    </div>
                    {(attempt.error || attempt.responseSnippet) && (
                      <p className="mt-1 break-words font-mono text-xs text-muted">{attempt.error ?? attempt.responseSnippet}</p>
                    )}
                  </li>
                ))}
              </ol>
            )}
          </div>

          <div>
            <h3 className="mb-2 text-sm font-semibold">Corpo enviado (assinado)</h3>
            <JsonBlock value={details.data.body} />
          </div>
        </div>
      )}
    </Drawer>
  )
}
