# ADR 0004: Autenticação por API key no Gateway

**Status:** aceito

## Contexto

Todos os endpoints públicos são multi-tenant e autenticados por API key. Replicar a validação em cada serviço espalharia a lógica e exigiria que todos conhecessem as chaves.

## Decisão

O Gateway (YARP) valida a chave na borda: calcula o SHA-256, resolve o tenant no Management (endpoint interno, fora do roteamento público) com cache de 60s (10s para chaves inválidas), descarta qualquer `X-Tenant-Id` vindo do cliente e injeta o header correto. Os serviços confiam no header porque só são alcançáveis pela rede privada. O rate limit por tenant (token bucket) usa o mesmo tenant resolvido.

## Consequências

- Os serviços ficam simples e testáveis (basta enviar `X-Tenant-Id` nos testes).
- Uma chave revogada pode continuar válida por até 60s. Aceito como troca por disponibilidade: se o Management cair, chaves conhecidas continuam funcionando.
- Exige que os serviços internos não sejam expostos publicamente (garantido pelos security groups no Terraform).
