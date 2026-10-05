import { describe, expect, it } from 'vitest'
import { formatDuration, formatNumber, formatPercent, formatRelative, shortId, successRate } from './format'

describe('format', () => {
  const now = new Date('2026-10-05T12:00:00Z').getTime()

  it('formata tempo relativo no passado e no futuro', () => {
    expect(formatRelative('2026-10-05T12:00:01Z', now)).toBe('agora')
    expect(formatRelative('2026-10-05T11:59:48Z', now)).toBe('há 12 s')
    expect(formatRelative('2026-10-05T11:57:00Z', now)).toBe('há 3 min')
    expect(formatRelative('2026-10-05T12:00:30Z', now)).toBe('em 30 s')
    expect(formatRelative('2026-10-05T18:00:00Z', now)).toBe('em 6 h')
  })

  it('formata duracoes em ms, s e min', () => {
    expect(formatDuration(42)).toBe('42 ms')
    expect(formatDuration(1500)).toBe('1,5 s')
    expect(formatDuration(180_000)).toBe('3 min')
  })

  it('formata percentuais e numeros no padrao brasileiro', () => {
    expect(formatPercent(0.9876)).toBe('98,8%')
    expect(formatNumber(1234)).toBe('1.234')
  })

  it('calcula taxa de sucesso e trata zero tentativas', () => {
    expect(successRate(9, 1)).toBe(0.9)
    expect(successRate(0, 0)).toBeNull()
  })

  it('encurta ids sem hifens', () => {
    expect(shortId('8f14e45f-ceea-4672-a0c2-9f3c5f3e1d01')).toBe('8f14e45f')
  })
})
