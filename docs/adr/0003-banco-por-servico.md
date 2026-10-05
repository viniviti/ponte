# ADR 0003: Um banco por serviço, PostgreSQL e SQL Server

**Status:** aceito

## Contexto

Ingestion e Delivery têm perfil de escrita intensa e append-only. Management tem dados relacionais de configuração, concorrência de edição humana e agregações para cobrança.

## Decisão

- **Ingestion e Delivery em PostgreSQL** (bancos separados): `jsonb`, `SKIP LOCKED`, índices parciais e custo baixo para volume alto.
- **Management em SQL Server**: representa o cenário comum de empresas com o "core" administrativo em SQL Server; usa `rowversion`, `MERGE` com `HOLDLOCK` e primitive collections JSON do EF Core 8.
- Nenhum serviço lê o banco de outro. O Delivery mantém uma réplica dos endpoints alimentada por `EndpointUpserted`/`EndpointDeleted` (event-carried state transfer), com versão monotônica para lidar com mensagens fora de ordem.

## Consequências

- Falhas isoladas: o Management fora do ar não para entregas.
- Consistência eventual entre o cadastro do endpoint e a primeira entrega (normalmente milissegundos).
- Dois motores de banco para operar. Mitigado com RDS gerenciado e o mesmo EF Core nos dois.
