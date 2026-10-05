// Teste de carga da ingestao (k6): https://k6.io
//   k6 run tests/load/ingest.js
//   k6 run -e RATE=2000 -e DURATION=2m tests/load/ingest.js
//
// Mede o caminho quente: Gateway (auth + rate limit) -> Ingestion (INSERT evento + outbox).
// A entrega acontece de forma assincrona e nao entra na latencia do cliente.

import http from 'k6/http'
import { check } from 'k6'

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5100'
const API_KEY = __ENV.API_KEY || 'pk_test_ponte_demo_key_0000000000000000'
const RATE = Number(__ENV.RATE || 500)

export const options = {
  scenarios: {
    ingest: {
      executor: 'ramping-arrival-rate',
      startRate: 50,
      timeUnit: '1s',
      preAllocatedVUs: 100,
      maxVUs: 1000,
      stages: [
        { target: RATE, duration: '30s' },
        { target: RATE, duration: __ENV.DURATION || '1m' },
        { target: 0, duration: '10s' },
      ],
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<60', 'p(99)<150'],
  },
}

const types = ['order.paid', 'order.refunded', 'invoice.payment_failed', 'user.signed_up']

export default function () {
  const eventType = types[Math.floor(Math.random() * types.length)]
  const body = JSON.stringify({
    eventType,
    payload: { id: `${__VU}-${__ITER}`, amount: Math.round(Math.random() * 100000) / 100, at: new Date().toISOString() },
  })

  const response = http.post(`${BASE_URL}/v1/events`, body, {
    headers: { 'Content-Type': 'application/json', 'X-Api-Key': API_KEY, 'Idempotency-Key': `${__VU}-${__ITER}` },
  })

  check(response, { 'aceito (202)': (r) => r.status === 202 })
}
