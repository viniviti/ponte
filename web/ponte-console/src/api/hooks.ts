import { keepPreviousData, useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useApi } from './session'
import type {
  ApiKeyView,
  CreatedApiKey,
  DeliveryDetails,
  DeliveryStatus,
  DeliverySummary,
  EndpointInput,
  EndpointSecret,
  EventDetails,
  EventSummary,
  Page,
  StatsOverview,
  Tenant,
  WebhookEndpoint,
} from './types'

export const queryKeys = {
  tenant: ['tenant'] as const,
  stats: (hours: number) => ['stats', hours] as const,
  endpoints: ['endpoints'] as const,
  events: (eventType: string) => ['events', eventType] as const,
  event: (id: string) => ['event', id] as const,
  deliveries: (filters: DeliveryFilters) => ['deliveries', filters] as const,
  delivery: (id: string) => ['delivery', id] as const,
  apiKeys: ['api-keys'] as const,
}

export interface DeliveryFilters {
  status?: DeliveryStatus | ''
  endpointId?: string
  eventId?: string
}

function qs(params: Record<string, string | number | undefined | null>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') search.set(key, String(value))
  }
  const text = search.toString()
  return text ? `?${text}` : ''
}

export function useTenant() {
  const api = useApi()
  return useQuery({ queryKey: queryKeys.tenant, queryFn: () => api<Tenant>('/v1/tenant'), staleTime: 5 * 60_000 })
}

export function useStats(hours = 24) {
  const api = useApi()
  return useQuery({
    queryKey: queryKeys.stats(hours),
    queryFn: () => api<StatsOverview>(`/v1/stats/overview${qs({ hours })}`),
    refetchInterval: 30_000,
  })
}

export function useEndpoints() {
  const api = useApi()
  return useQuery({ queryKey: queryKeys.endpoints, queryFn: () => api<WebhookEndpoint[]>('/v1/endpoints') })
}

export function useSaveEndpoint() {
  const api = useApi()
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ id, input }: { id?: string; input: EndpointInput }) =>
      id
        ? api<WebhookEndpoint>(`/v1/endpoints/${id}`, { method: 'PUT', body: input })
        : api<WebhookEndpoint>('/v1/endpoints', { method: 'POST', body: input }),
    onSuccess: () => client.invalidateQueries({ queryKey: queryKeys.endpoints }),
  })
}

export function useDeleteEndpoint() {
  const api = useApi()
  const client = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api<void>(`/v1/endpoints/${id}`, { method: 'DELETE' }),
    onSuccess: () => client.invalidateQueries({ queryKey: queryKeys.endpoints }),
  })
}

export function useEndpointSecret() {
  const api = useApi()
  return useMutation({ mutationFn: (id: string) => api<EndpointSecret>(`/v1/endpoints/${id}/secret`) })
}

export function useRotateSecret() {
  const api = useApi()
  const client = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api<EndpointSecret>(`/v1/endpoints/${id}/rotate-secret`, { method: 'POST' }),
    onSuccess: () => client.invalidateQueries({ queryKey: queryKeys.endpoints }),
  })
}

export function useEvents(eventType: string) {
  const api = useApi()
  return useInfiniteQuery({
    queryKey: queryKeys.events(eventType),
    queryFn: ({ pageParam }) => api<Page<EventSummary>>(`/v1/events${qs({ eventType, cursor: pageParam, limit: 25 })}`),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
    placeholderData: keepPreviousData,
  })
}

export function useEvent(id: string | undefined) {
  const api = useApi()
  return useQuery({
    queryKey: queryKeys.event(id ?? ''),
    queryFn: () => api<EventDetails>(`/v1/events/${id}`),
    enabled: Boolean(id),
  })
}

export function useSendEvent() {
  const api = useApi()
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ eventType, payload, idempotencyKey }: { eventType: string; payload: unknown; idempotencyKey?: string }) =>
      api<{ id: string; receivedAt: string }>('/v1/events', {
        method: 'POST',
        body: { eventType, payload },
        headers: idempotencyKey ? { 'Idempotency-Key': idempotencyKey } : undefined,
      }),
    onSuccess: () => client.invalidateQueries({ queryKey: ['events'] }),
  })
}

export function useDeliveries(filters: DeliveryFilters) {
  const api = useApi()
  return useInfiniteQuery({
    queryKey: queryKeys.deliveries(filters),
    queryFn: ({ pageParam }) =>
      api<Page<DeliverySummary>>(
        `/v1/deliveries${qs({ status: filters.status, endpointId: filters.endpointId, eventId: filters.eventId, cursor: pageParam, limit: 30 })}`,
      ),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
    placeholderData: keepPreviousData,
  })
}

export function useDelivery(id: string | undefined) {
  const api = useApi()
  return useQuery({
    queryKey: queryKeys.delivery(id ?? ''),
    queryFn: () => api<DeliveryDetails>(`/v1/deliveries/${id}`),
    enabled: Boolean(id),
  })
}

export function useReplayDelivery() {
  const api = useApi()
  const client = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api<void>(`/v1/deliveries/${id}/replay`, { method: 'POST' }),
    onSuccess: (_, id) => {
      void client.invalidateQueries({ queryKey: ['deliveries'] })
      void client.invalidateQueries({ queryKey: queryKeys.delivery(id) })
    },
  })
}

export function useReplayAllDead() {
  const api = useApi()
  const client = useQueryClient()
  return useMutation({
    mutationFn: (endpointId?: string) =>
      api<{ replayed: number }>(`/v1/deliveries/replay-dead${qs({ endpointId })}`, { method: 'POST' }),
    onSuccess: () => client.invalidateQueries({ queryKey: ['deliveries'] }),
  })
}

export function useApiKeys() {
  const api = useApi()
  return useQuery({ queryKey: queryKeys.apiKeys, queryFn: () => api<ApiKeyView[]>('/v1/api-keys') })
}

export function useCreateApiKey() {
  const api = useApi()
  const client = useQueryClient()
  return useMutation({
    mutationFn: (name: string) => api<CreatedApiKey>('/v1/api-keys', { method: 'POST', body: { name } }),
    onSuccess: () => client.invalidateQueries({ queryKey: queryKeys.apiKeys }),
  })
}

export function useRevokeApiKey() {
  const api = useApi()
  const client = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api<void>(`/v1/api-keys/${id}`, { method: 'DELETE' }),
    onSuccess: () => client.invalidateQueries({ queryKey: queryKeys.apiKeys }),
  })
}
