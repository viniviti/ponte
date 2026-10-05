import { Link } from 'react-router-dom'
import { useEndpoints, useStats } from '../api/hooks'
import type { LiveDeliveryAttempt } from '../api/types'
import { HourlyChart } from '../components/HourlyChart'
import { LiveIndicator, useLive } from '../components/Layout'
import { Card, EmptyState, ErrorNotice, HttpStatus, Kpi, PageHeader, Spinner, StatusBadge } from '../components/ui'
import { formatDuration, formatNumber, formatPercent, formatRelative, shortId } from '../lib/format'
import { SendEventButton } from './EventsPage'

export function OverviewPage() {
  const stats = useStats(24)
  const endpoints = useEndpoints()
  const { feed, state } = useLive()
  const endpointUrls = new Map(endpoints.data?.map((e) => [e.id, e.url]))

  return (
    <>
      <PageHeader
        title="Visão geral"
        description="Tudo o que saiu pela ponte nas últimas 24 horas. O feed ao vivo chega por SignalR, direto do RabbitMQ."
        action={<SendEventButton />}
      />

      <ErrorNotice error={stats.error} />

      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        <Kpi label="Tentativas" value={stats.data ? formatNumber(stats.data.attempts) : '–'} hint="últimas 24h" />
        <Kpi
          label="Taxa de sucesso"
          value={stats.data ? formatPercent(stats.data.successRate) : '–'}
          tone={stats.data && stats.data.successRate < 0.9 ? 'warning' : 'success'}
          hint="por tentativa"
        />
        <Kpi label="Latência média" value={stats.data ? formatDuration(stats.data.averageLatencyMs) : '–'} hint="resposta do cliente" />
        <Kpi
          label="Mortas"
          value={stats.data ? formatNumber(stats.data.dead) : '–'}
          tone={stats.data && stats.data.dead > 0 ? 'danger' : undefined}
          hint={<Link to="/deliveries?status=dead" className="underline-offset-2 hover:underline">ver e reenviar</Link>}
        />
      </div>

      <div className="mt-6 grid gap-6 lg:grid-cols-[1fr_380px]">
        <Card title="Tentativas por hora">
          <div className="p-5">{stats.isLoading ? <Spinner /> : stats.data && <HourlyChart series={stats.data.series} />}</div>
        </Card>

        <Card title="Ao vivo" action={<LiveIndicator state={state} />}>
          {feed.length === 0 ? (
            <EmptyState title="Aguardando tentativas">Envie um evento de teste e acompanhe cada tentativa chegando aqui.</EmptyState>
          ) : (
            <ul className="max-h-[420px] divide-y divide-line overflow-y-auto">
              {feed.map((attempt) => (
                <FeedItem key={`${attempt.deliveryId}-${attempt.attemptNumber}`} attempt={attempt} endpointUrl={endpointUrls.get(attempt.endpointId)} />
              ))}
            </ul>
          )}
        </Card>
      </div>
    </>
  )
}

function FeedItem({ attempt, endpointUrl }: { attempt: LiveDeliveryAttempt; endpointUrl?: string }) {
  return (
    <li className="feed-item px-5 py-3">
      <div className="flex items-center justify-between gap-3">
        <span className="truncate font-mono text-sm">{attempt.eventType}</span>
        <StatusBadge status={attempt.outcome} />
      </div>
      <div className="mt-1 flex items-center gap-3 text-xs text-muted">
        <HttpStatus code={attempt.statusCode} />
        <span className="tabular">{formatDuration(attempt.durationMs)}</span>
        <span>#{attempt.attemptNumber}</span>
        <span className="truncate" title={endpointUrl}>
          {endpointUrl ? new URL(endpointUrl).pathname : shortId(attempt.endpointId)}
        </span>
        <span className="ml-auto shrink-0">
          {attempt.nextAttemptAt ? `retry ${formatRelative(attempt.nextAttemptAt)}` : formatRelative(attempt.attemptedAt)}
        </span>
      </div>
    </li>
  )
}
