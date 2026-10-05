const numberFormat = new Intl.NumberFormat('pt-BR')
const compactFormat = new Intl.NumberFormat('pt-BR', { notation: 'compact', maximumFractionDigits: 1 })

export function formatNumber(value: number): string {
  return Math.abs(value) >= 10_000 ? compactFormat.format(value) : numberFormat.format(value)
}

export function formatPercent(ratio: number, digits = 1): string {
  return `${(ratio * 100).toFixed(digits).replace('.', ',')}%`
}

export function formatDuration(ms: number): string {
  if (ms < 1000) return `${Math.round(ms)} ms`
  if (ms < 60_000) return `${(ms / 1000).toFixed(1).replace('.', ',')} s`
  return `${Math.round(ms / 60_000)} min`
}

/** "agora", "há 12 s", "há 3 min", "em 30 s" (relativo a `now`). */
export function formatRelative(iso: string, now: number = Date.now()): string {
  const diff = Math.round((new Date(iso).getTime() - now) / 1000)
  const abs = Math.abs(diff)
  if (abs < 3) return 'agora'

  const [value, unit] =
    abs < 60 ? [abs, 's'] : abs < 3600 ? [Math.round(abs / 60), 'min'] : abs < 86_400 ? [Math.round(abs / 3600), 'h'] : [Math.round(abs / 86_400), 'd']

  return diff < 0 ? `há ${value} ${unit}` : `em ${value} ${unit}`
}

export function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'medium' })
}

export function formatHour(iso: string): string {
  return new Date(iso).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })
}

/** Ids ficam longos na UI: mostra o comeco, o resto vai no title. */
export function shortId(id: string, size = 8): string {
  return id.replace(/-/g, '').slice(0, size)
}

export function successRate(succeeded: number, failed: number): number | null {
  const total = succeeded + failed
  return total === 0 ? null : succeeded / total
}
