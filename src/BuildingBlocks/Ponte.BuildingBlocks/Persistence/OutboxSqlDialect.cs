namespace Ponte.BuildingBlocks.Persistence;

/// <summary>
/// Strategy: cada banco tem sua forma de "pegar um lote e pular o que outra instancia
/// ja travou". E isso que permite rodar N replicas do relay sem publicar em dobro.
/// </summary>
public interface IOutboxSqlDialect
{
    /// <summary>SQL com um parametro posicional {0} = tamanho do lote.</summary>
    string LockPendingBatchSql { get; }
}

public sealed class PostgresOutboxDialect : IOutboxSqlDialect
{
    public string LockPendingBatchSql =>
        """
        SELECT * FROM outbox_messages
        WHERE processed_at IS NULL AND attempts < 25
        ORDER BY created_at
        LIMIT {0}
        FOR UPDATE SKIP LOCKED
        """;
}

public sealed class SqlServerOutboxDialect : IOutboxSqlDialect
{
    public string LockPendingBatchSql =>
        """
        SELECT TOP ({0}) * FROM outbox_messages WITH (UPDLOCK, READPAST, ROWLOCK)
        WHERE processed_at IS NULL AND attempts < 25
        ORDER BY created_at
        """;
}
