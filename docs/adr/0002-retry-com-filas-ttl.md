# ADR 0002: Retry com filas de espera por degrau (TTL + dead-letter)

**Status:** aceito

## Contexto

Entregas que falham precisam ser repetidas com backoff exponencial ao longo de horas. Opções consideradas:

1. `Task.Delay` em memória: perde tudo se a instância cair e segura recursos.
2. Polling no banco (`WHERE next_attempt_at <= now()`): funciona, mas gera carga constante e exige coordenação entre instâncias.
3. Plugin `rabbitmq_delayed_message_exchange`: não é suportado no Amazon MQ e não é replicado.
4. Filas sem consumidor com TTL e dead-letter de volta para a fila de trabalho.

## Decisão

Opção 4, com uma fila por degrau (5s, 30s, 2m, 10m, 1h, 6h). O handler escolhe o menor degrau que cobre o atraso desejado (inclusive para `Retry-After` e para adiamentos do circuit breaker).

## Consequências

- O agendamento é durável e replicado (quorum queues) sem nenhum componente extra.
- Atrasos ficam restritos aos degraus. Para webhooks isso é irrelevante.
- Mudar o TTL de uma fila existente exige recriá-la (o RabbitMQ não altera argumentos de fila). Novos degraus entram com novos nomes.
