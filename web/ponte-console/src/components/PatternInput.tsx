import { X } from 'lucide-react'
import { useState, type KeyboardEvent } from 'react'
import { isValidPattern } from '../lib/eventPattern'
import { inputClass } from './ui'

/** Campo de "chips" para os padroes de evento (order.*, invoice.#, #). */
export function PatternInput({ value, onChange }: { value: string[]; onChange: (next: string[]) => void }) {
  const [draft, setDraft] = useState('')
  const [error, setError] = useState<string | null>(null)

  function commit() {
    const pattern = draft.trim().toLowerCase()
    if (!pattern) return
    if (!isValidPattern(pattern)) {
      setError(`"${pattern}" não é um padrão válido`)
      return
    }
    if (!value.includes(pattern)) onChange([...value, pattern])
    setDraft('')
    setError(null)
  }

  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'Enter' || event.key === ',' || event.key === ' ') {
      event.preventDefault()
      commit()
    } else if (event.key === 'Backspace' && !draft && value.length > 0) {
      onChange(value.slice(0, -1))
    }
  }

  return (
    <div>
      <div className="flex flex-wrap gap-1.5 pb-2">
        {value.map((pattern) => (
          <span key={pattern} className="inline-flex items-center gap-1 rounded border border-line bg-surface-2 px-2 py-0.5 font-mono text-xs">
            {pattern}
            <button type="button" aria-label={`Remover ${pattern}`} onClick={() => onChange(value.filter((p) => p !== pattern))} className="text-muted hover:text-danger">
              <X className="size-3" />
            </button>
          </span>
        ))}
      </div>
      <input
        className={inputClass}
        value={draft}
        placeholder="order.*  invoice.#  #"
        onChange={(event) => setDraft(event.target.value)}
        onKeyDown={onKeyDown}
        onBlur={commit}
        aria-label="Padrões de evento"
      />
      {error && <p className="mt-1 text-xs text-danger">{error}</p>}
    </div>
  )
}
