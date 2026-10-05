import { Send } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { useDeliveries, useEvent, useEvents, useSendEvent } from '../api/hooks'
import { Button, Card, Drawer, EmptyState, ErrorNotice, Field, JsonBlock, PageHeader, Spinner, StatusBadge, inputClass } from '../components/ui'
import { isValidEventType } from '../lib/eventPattern'
import { formatDateTime, formatRelative, shortId } from '../lib/format'

const samples: Record<string, unknown> = {
  'order.paid': { orderId: 'PED-1042', customer: { name: 'Ana Souza', email: 'ana@example.com' }, total: 389.9, currency: 'BRL', method: 'pix' },
  'order.refunded': { orderId: 'PED-1042', amount: 389.9, reason: 'customer_request' },
  'invoice.payment_failed': { invoiceId: 'NF-2025-88', attempt: 2, reason: 'card_declined' },
}

export function EventsPage() {
  const [filter, setFilter] = useState('')
  const [params, setParams] = useSearchParams()
  const selected = params.get('id') ?? undefined
  const events = useEvents(filter.trim())
  const items = events.data?.pages.flatMap((page) => page.items) ?? []

  return (
    <>
      <PageHeader
        title="Eventos"
        description="Cada evento aceito pela API de ingestão. Um evento vira uma entrega para cada endpoint inscrito no tipo dele."
        action={<SendEventButton />}
      />

      <Card
        title="Recebidos"
        action={
          <input
            className={`${inputClass} h-8 w-56 font-mono text-xs`}
            placeholder="filtrar por tipo exato"
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            aria-label="Filtrar por tipo de evento"
          />
        }
      >
        {events.isLoading ? (
          <Spinner />
        ) : items.length === 0 ? (
          <EmptyState title="Nenhum evento ainda">Use "Enviar evento de teste" ou faça um POST em /v1/events com sua API key.</EmptyState>
        ) : (
          <table className="w-full text-sm">
            <thead className="text-left text-xs uppercase tracking-wider text-muted">
              <tr>
                <th className="px-5 py-2.5 font-medium">Tipo</th>
                <th className="px-5 py-2.5 font-medium">Id</th>
                <th className="px-5 py-2.5 text-right font-medium">Recebido</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-line">
              {items.map((event) => (
                <tr key={event.id} className="cursor-pointer hover:bg-surface-2" onClick={() => setParams({ id: event.id })}>
                  <td className="px-5 py-3 font-mono">{event.eventType}</td>
                  <td className="px-5 py-3 font-mono text-xs text-muted" title={event.id}>{shortId(event.id)}</td>
                  <td className="px-5 py-3 text-right text-muted" title={formatDateTime(event.receivedAt)}>{formatRelative(event.receivedAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {events.hasNextPage && (
          <div className="border-t border-line p-3 text-center">
            <Button variant="ghost" onClick={() => void events.fetchNextPage()} loading={events.isFetchingNextPage}>Carregar mais</Button>
          </div>
        )}
      </Card>

      <EventDrawer id={selected} onClose={() => setParams({})} />
    </>
  )
}

function EventDrawer({ id, onClose }: { id?: string; onClose: () => void }) {
  const event = useEvent(id)
  const deliveries = useDeliveries({ eventId: id })
  const items = deliveries.data?.pages.flatMap((p) => p.items) ?? []

  return (
    <Drawer open={Boolean(id)} title={event.data?.eventType ?? 'Evento'} onClose={onClose}>
      {event.isLoading ? (
        <Spinner />
      ) : event.data ? (
        <div className="space-y-6">
          <dl className="grid grid-cols-[120px_1fr] gap-y-2 text-sm">
            <dt className="text-muted">Id</dt>
            <dd className="font-mono text-xs">{event.data.id}</dd>
            <dt className="text-muted">Recebido</dt>
            <dd>{formatDateTime(event.data.receivedAt)}</dd>
            <dt className="text-muted">Idempotency-Key</dt>
            <dd className="font-mono text-xs">{event.data.idempotencyKey ?? '–'}</dd>
          </dl>
          <div>
            <h3 className="mb-2 text-sm font-semibold">Payload</h3>
            <JsonBlock value={event.data.payload} />
          </div>
          <div>
            <h3 className="mb-2 text-sm font-semibold">Entregas ({items.length})</h3>
            {items.length === 0 ? (
              <p className="text-sm text-muted">Nenhum endpoint inscrito neste tipo (ou o fan-out ainda está acontecendo).</p>
            ) : (
              <ul className="divide-y divide-line rounded-md border border-line">
                {items.map((delivery) => (
                  <li key={delivery.id} className="flex items-center justify-between gap-3 px-3 py-2.5 text-sm">
                    <Link to={`/deliveries?id=${delivery.id}`} className="font-mono text-xs hover:text-accent">{shortId(delivery.id)}</Link>
                    <span className="text-xs text-muted">{delivery.attemptCount} tentativa(s)</span>
                    <StatusBadge status={delivery.status} />
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>
      ) : (
        <ErrorNotice error={event.error} />
      )}
    </Drawer>
  )
}

export function SendEventButton() {
  const [open, setOpen] = useState(false)
  return (
    <>
      <Button variant="primary" onClick={() => setOpen(true)}>
        <Send className="size-4" aria-hidden />
        Enviar evento de teste
      </Button>
      <Drawer open={open} title="Enviar evento de teste" onClose={() => setOpen(false)}>
        <SendEventForm onSent={() => setOpen(false)} />
      </Drawer>
    </>
  )
}

export function SendEventForm({ onSent }: { onSent?: (id: string) => void }) {
  const send = useSendEvent()
  const [eventType, setEventType] = useState('order.paid')
  const [payload, setPayload] = useState(JSON.stringify(samples['order.paid'], null, 2))
  const [idempotencyKey, setIdempotencyKey] = useState('')
  const [burst, setBurst] = useState(1)
  const [localError, setLocalError] = useState<string | null>(null)

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    setLocalError(null)

    if (!isValidEventType(eventType)) {
      setLocalError('Use o formato dominio.acao em minúsculas (ex.: order.paid).')
      return
    }

    let parsed: unknown
    try {
      parsed = JSON.parse(payload)
    } catch {
      setLocalError('O payload não é um JSON válido.')
      return
    }

    let lastId = ''
    try {
      for (let i = 0; i < burst; i++) {
        const result = await send.mutateAsync({ eventType, payload: parsed, idempotencyKey: idempotencyKey || undefined })
        lastId = result.id
      }
    } catch {
      return // o erro aparece via send.error
    }
    onSent?.(lastId)
  }

  return (
    <form onSubmit={onSubmit} className="space-y-4">
      <Field label="Tipo do evento" hint="Exemplos prontos:">
        <input className={`${inputClass} font-mono`} value={eventType} onChange={(e) => setEventType(e.target.value)} aria-label="Tipo do evento" />
      </Field>
      <div className="-mt-2 flex flex-wrap gap-1.5">
        {Object.keys(samples).map((type) => (
          <button
            key={type}
            type="button"
            className="rounded border border-line px-2 py-0.5 font-mono text-xs text-muted hover:border-accent hover:text-fg"
            onClick={() => {
              setEventType(type)
              setPayload(JSON.stringify(samples[type], null, 2))
            }}
          >
            {type}
          </button>
        ))}
      </div>
      <Field label="Payload (JSON)">
        <textarea className={`${inputClass} h-56 font-mono text-xs`} value={payload} onChange={(e) => setPayload(e.target.value)} spellCheck={false} aria-label="Payload" />
      </Field>
      <div className="grid grid-cols-2 gap-3">
        <Field label="Idempotency-Key" hint="Repita para ver a deduplicação">
          <input className={`${inputClass} font-mono text-xs`} value={idempotencyKey} onChange={(e) => setIdempotencyKey(e.target.value)} placeholder="opcional" />
        </Field>
        <Field label="Quantidade" hint="Rajada para ver o fan-out">
          <input type="number" min={1} max={200} className={inputClass} value={burst} onChange={(e) => setBurst(Math.min(200, Math.max(1, Number(e.target.value) || 1)))} />
        </Field>
      </div>
      {localError && <p className="text-sm text-danger" role="alert">{localError}</p>}
      <ErrorNotice error={send.error} />
      <Button type="submit" variant="primary" loading={send.isPending} className="w-full">
        Enviar {burst > 1 ? `${burst} eventos` : 'evento'}
      </Button>
    </form>
  )
}
