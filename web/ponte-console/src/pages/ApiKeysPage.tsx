import { useState, type FormEvent } from 'react'
import { useApiKeys, useCreateApiKey, useRevokeApiKey } from '../api/hooks'
import type { CreatedApiKey } from '../api/types'
import { Button, Card, CopyButton, EmptyState, ErrorNotice, PageHeader, Spinner, inputClass } from '../components/ui'
import { formatDateTime } from '../lib/format'

export function ApiKeysPage() {
  const keys = useApiKeys()
  const create = useCreateApiKey()
  const revoke = useRevokeApiKey()
  const [name, setName] = useState('')
  const [created, setCreated] = useState<CreatedApiKey | null>(null)

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    try {
      setCreated(await create.mutateAsync(name))
      setName('')
    } catch {
      // erro exibido abaixo
    }
  }

  return (
    <>
      <PageHeader
        title="Chaves de API"
        description="Guardamos apenas o hash SHA-256: a chave completa aparece uma única vez, na criação."
      />

      <Card className="mb-6">
        <form onSubmit={onSubmit} className="flex flex-wrap items-end gap-3 p-5">
          <label className="min-w-60 flex-1">
            <span className="mb-1.5 block text-sm font-medium">Nome da chave</span>
            <input className={inputClass} value={name} onChange={(e) => setName(e.target.value)} placeholder="Backend de produção" required maxLength={80} />
          </label>
          <Button type="submit" variant="primary" loading={create.isPending}>Gerar chave</Button>
        </form>
        {created && (
          <div className="mx-5 mb-5 rounded-md border border-warning/40 bg-warning/10 p-4">
            <div className="flex items-center justify-between gap-2">
              <p className="text-sm font-medium text-warning">Copie agora: esta chave não será exibida de novo.</p>
              <CopyButton value={created.key} />
            </div>
            <p className="mt-2 break-all font-mono text-sm">{created.key}</p>
          </div>
        )}
        <div className="px-5 pb-5"><ErrorNotice error={create.error ?? revoke.error} /></div>
      </Card>

      <Card title="Chaves">
        {keys.isLoading ? (
          <Spinner />
        ) : !keys.data?.length ? (
          <EmptyState title="Nenhuma chave" />
        ) : (
          <ul className="divide-y divide-line">
            {keys.data.map((key) => (
              <li key={key.id} className="flex flex-wrap items-center gap-4 px-5 py-3.5">
                <div className="min-w-0 flex-1">
                  <p className="font-medium">{key.name}</p>
                  <p className="font-mono text-xs text-muted">{key.displayPrefix}…</p>
                </div>
                <span className="text-xs text-muted">criada {formatDateTime(key.createdAt)}</span>
                {key.revokedAt ? (
                  <span className="rounded-full border border-line px-2 py-0.5 text-xs text-muted">Revogada</span>
                ) : (
                  <Button variant="danger" onClick={() => revoke.mutate(key.id)} loading={revoke.isPending && revoke.variables === key.id}>
                    Revogar
                  </Button>
                )}
              </li>
            ))}
          </ul>
        )}
      </Card>
    </>
  )
}
