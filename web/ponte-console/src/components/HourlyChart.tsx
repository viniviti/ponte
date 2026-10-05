import { useMemo, useState } from 'react'
import type { HourlyPoint } from '../api/types'
import { formatHour, formatNumber } from '../lib/format'

/**
 * Barras empilhadas por hora (entregues x falhas) em SVG puro: sem biblioteca de
 * grafico, leve e com o mesmo tema do resto do console.
 */
export function HourlyChart({ series }: { series: HourlyPoint[] }) {
  const [hover, setHover] = useState<number | null>(null)
  const max = useMemo(() => Math.max(1, ...series.map((p) => p.succeeded + p.failed)), [series])

  const width = 720
  const height = 180
  const gap = 3
  const barWidth = series.length > 0 ? width / series.length - gap : 0
  const active = hover !== null ? series[hover] : undefined

  return (
    <div className="relative">
      <div className="mb-3 flex items-center gap-4 text-xs text-muted">
        <Legend className="bg-success" label="Entregues" />
        <Legend className="bg-danger" label="Falhas" />
        <span className="ml-auto font-mono tabular" aria-live="polite">
          {active
            ? `${formatHour(active.hour)} · ${formatNumber(active.succeeded)} ok · ${formatNumber(active.failed)} falhas`
            : `pico ${formatNumber(max)}/h`}
        </span>
      </div>
      <svg viewBox={`0 0 ${width} ${height}`} className="h-44 w-full" preserveAspectRatio="none" role="img" aria-label="Tentativas de entrega por hora">
        {[0.25, 0.5, 0.75].map((ratio) => (
          <line key={ratio} x1={0} x2={width} y1={height * ratio} y2={height * ratio} stroke="var(--border)" strokeDasharray="2 4" />
        ))}
        {series.map((point, index) => {
          const x = index * (barWidth + gap)
          const okHeight = (point.succeeded / max) * (height - 4)
          const failHeight = (point.failed / max) * (height - 4)
          const dim = hover !== null && hover !== index
          return (
            <g key={point.hour} opacity={dim ? 0.45 : 1} onMouseEnter={() => setHover(index)} onMouseLeave={() => setHover(null)}>
              <rect x={x} y={0} width={barWidth + gap} height={height} fill="transparent" />
              <rect x={x} y={height - okHeight} width={barWidth} height={okHeight} rx={1.5} fill="var(--success)" />
              <rect x={x} y={height - okHeight - failHeight} width={barWidth} height={failHeight} rx={1.5} fill="var(--danger)" />
            </g>
          )
        })}
      </svg>
      <div className="mt-2 flex justify-between font-mono text-[11px] text-muted tabular">
        <span>{series[0] ? formatHour(series[0].hour) : ''}</span>
        <span>{series.at(-1) ? formatHour(series.at(-1)!.hour) : ''}</span>
      </div>
    </div>
  )
}

function Legend({ className, label }: { className: string; label: string }) {
  return (
    <span className="inline-flex items-center gap-1.5">
      <span className={`size-2 rounded-sm ${className}`} aria-hidden />
      {label}
    </span>
  )
}
