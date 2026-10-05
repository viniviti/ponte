// Mesmo contrato do backend (Ponte.BuildingBlocks.Routing.EventTypePattern):
// "*" casa um segmento, "#" casa zero ou mais.

const PATTERN = /^(\*|#|[a-z0-9_-]+)(\.(\*|#|[a-z0-9_-]+))*$/
const EVENT_TYPE = /^[a-z0-9_-]+(\.[a-z0-9_-]+)*$/

export function isValidPattern(pattern: string): boolean {
  return pattern.length > 0 && pattern.length <= 128 && PATTERN.test(pattern)
}

export function isValidEventType(eventType: string): boolean {
  return eventType.length > 0 && eventType.length <= 128 && EVENT_TYPE.test(eventType)
}

export function matches(pattern: string, eventType: string): boolean {
  if (pattern === '#') return true
  return match(pattern.split('.'), 0, eventType.split('.'), 0)
}

function match(pattern: string[], p: number, words: string[], w: number): boolean {
  while (true) {
    if (p === pattern.length) return w === words.length
    const segment = pattern[p]
    if (segment === '#') {
      if (p === pattern.length - 1) return true
      for (let skip = w; skip <= words.length; skip++) {
        if (match(pattern, p + 1, words, skip)) return true
      }
      return false
    }
    if (w === words.length) return false
    if (segment !== '*' && segment !== words[w]) return false
    p++
    w++
  }
}
