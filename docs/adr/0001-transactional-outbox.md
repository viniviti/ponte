# ADR 0001: Transactional Outbox para publicar eventos

**Status:** aceito

## Contexto

A ingestão precisa gravar o evento no PostgreSQL e avisar o Delivery pelo RabbitMQ. Fazer as duas coisas em sequência ("dual write") abre duas falhas: gravar e não publicar (evento perdido) ou publicar e não gravar (evento fantasma).

## Decisão

Gravar a mensagem em `outbox_messages` na mesma transação do evento. Um `BackgroundService` (relay) lê lotes pendentes com `FOR UPDATE SKIP LOCKED` (PostgreSQL) ou `UPDLOCK, READPAST` (SQL Server), publica com publisher confirms e marca como processado. O mesmo componente é reutilizado pelos três serviços, com o dialeto SQL como Strategy.

## Consequências

- A API responde depois de um único commit; o broker pode estar fora do ar sem afetar a ingestão.
- Entrega at-least-once: todo consumidor precisa ser idempotente (ADR implícito: Inbox).
- Latência extra entre o commit e a publicação (intervalo de polling, 200 a 250 ms por padrão). Aceitável para webhooks; reduzível com `LISTEN/NOTIFY` se necessário.
- N réplicas do relay podem rodar em paralelo sem duplicar publicações (testado).
