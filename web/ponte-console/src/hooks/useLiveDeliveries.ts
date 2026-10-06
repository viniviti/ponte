import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState } from 'react'
import { useSession } from '../api/session'
import { getDemoEngine } from '../demo/engine'
import type { LiveDeliveryAttempt } from '../api/types'

export type LiveState = 'connecting' | 'live' | 'reconnecting' | 'offline'

const MAX_FEED = 60

/**
 * Feed em tempo real das tentativas de entrega (SignalR via Gateway).
 * Tambem invalida as consultas de entregas/estatisticas, com debounce, para as
 * outras telas ficarem atualizadas sem polling agressivo.
 */
export function useLiveDeliveries() {
  const { settings } = useSession()
  const client = useQueryClient()
  const [feed, setFeed] = useState<LiveDeliveryAttempt[]>([])
  const [state, setState] = useState<LiveState>('offline')
  const refreshTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)

  useEffect(() => {
    if (!settings) return

    const onAttempt = (attempt: LiveDeliveryAttempt) => {
      setFeed((current) => [attempt, ...current].slice(0, MAX_FEED))

      clearTimeout(refreshTimer.current)
      refreshTimer.current = setTimeout(() => {
        void client.invalidateQueries({ queryKey: ['deliveries'] })
        void client.invalidateQueries({ queryKey: ['stats'] })
        void client.invalidateQueries({ queryKey: ['endpoints'] })
      }, 1500)
    }

    if (settings.demo) {
      // Na demonstracao o "RabbitMQ" e o motor simulado no proprio navegador.
      setState('live')
      const unsubscribe = getDemoEngine().subscribe(onAttempt)
      return () => {
        unsubscribe()
        clearTimeout(refreshTimer.current)
      }
    }

    const connection: HubConnection = new HubConnectionBuilder()
      .withUrl(`${settings.apiUrl.replace(/\/+$/, '')}/hubs/deliveries`, {
        accessTokenFactory: () => settings.apiKey,
      })
      .withAutomaticReconnect([0, 2000, 5000, 10_000, 30_000])
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('DeliveryAttempted', onAttempt)

    connection.onreconnecting(() => setState('reconnecting'))
    connection.onreconnected(() => setState('live'))
    connection.onclose(() => setState('offline'))

    let disposed = false
    setState('connecting')
    connection
      .start()
      .then(() => !disposed && setState('live'))
      .catch(() => !disposed && setState('offline'))

    return () => {
      disposed = true
      clearTimeout(refreshTimer.current)
      if (connection.state !== HubConnectionState.Disconnected) void connection.stop()
    }
  }, [settings, client])

  return { feed, state }
}
