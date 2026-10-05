import { describe, expect, it } from 'vitest'
import { isValidEventType, isValidPattern, matches } from './eventPattern'

// Mesmos casos do backend (EventTypePatternTests.cs): front e back precisam concordar.
describe('eventPattern', () => {
  it.each([
    ['order.paid', 'order.paid', true],
    ['order.*', 'order.paid', true],
    ['order.*', 'order.items.added', false],
    ['order.#', 'order', true],
    ['order.#', 'order.items.added', true],
    ['order.#.added', 'order.added', true],
    ['order.#.added', 'order.items.removed', false],
    ['#', 'qualquer.coisa', true],
  ])('%s casa %s => %s', (pattern, eventType, expected) => {
    expect(matches(pattern, eventType)).toBe(expected)
  })

  it('valida a sintaxe dos padroes', () => {
    expect(isValidPattern('order.*')).toBe(true)
    expect(isValidPattern('#')).toBe(true)
    expect(isValidPattern('Order.Paid')).toBe(false)
    expect(isValidPattern('order..paid')).toBe(false)
    expect(isValidPattern('')).toBe(false)
  })

  it('tipo de evento concreto nao aceita curingas', () => {
    expect(isValidEventType('invoice.payment_failed')).toBe(true)
    expect(isValidEventType('order.*')).toBe(false)
  })
})
