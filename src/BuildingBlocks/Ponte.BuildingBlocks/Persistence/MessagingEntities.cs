using Microsoft.EntityFrameworkCore;

namespace Ponte.BuildingBlocks.Persistence;

/// <summary>
/// Mensagem gravada na MESMA transacao da mudanca de negocio (Transactional Outbox).
/// Isso elimina o problema de "dual write": ou o dado e a mensagem existem juntos, ou nenhum.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; }

    public string Exchange { get; init; } = string.Empty;

    public string RoutingKey { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public string Payload { get; init; } = string.Empty;

    public string? TraceParent { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }
}

/// <summary>Registro do Inbox: garante que cada mensagem e processada uma unica vez por consumidor.</summary>
public sealed class ProcessedMessage
{
    public Guid MessageId { get; init; }

    public string Consumer { get; init; } = string.Empty;

    public DateTimeOffset ProcessedAt { get; init; }
}

public static class MessagingModelBuilderExtensions
{
    /// <summary>
    /// Mapeia outbox_messages e processed_messages com nomes explicitos, porque o relay
    /// usa SQL cru (FOR UPDATE SKIP LOCKED / UPDLOCK, READPAST) sobre essas tabelas.
    /// </summary>
    public static ModelBuilder AddMessagingTables(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(builder =>
        {
            builder.ToTable("outbox_messages");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.Property(x => x.Exchange).HasColumnName("exchange").HasMaxLength(128).IsRequired();
            builder.Property(x => x.RoutingKey).HasColumnName("routing_key").HasMaxLength(256).IsRequired();
            builder.Property(x => x.Type).HasColumnName("type").HasMaxLength(256).IsRequired();
            builder.Property(x => x.Payload).HasColumnName("payload").IsRequired();
            builder.Property(x => x.TraceParent).HasColumnName("trace_parent").HasMaxLength(64);
            builder.Property(x => x.CreatedAt).HasColumnName("created_at");
            builder.Property(x => x.ProcessedAt).HasColumnName("processed_at");
            builder.Property(x => x.Attempts).HasColumnName("attempts");
            builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(2000);

            // Indice parcial: o relay so enxerga o que esta pendente, entao o indice fica
            // pequeno mesmo com milhoes de mensagens ja processadas.
            builder.HasIndex(x => x.CreatedAt)
                .HasDatabaseName("ix_outbox_messages_pending")
                .HasFilter("processed_at IS NULL");
        });

        modelBuilder.Entity<ProcessedMessage>(builder =>
        {
            builder.ToTable("processed_messages");
            builder.HasKey(x => new { x.MessageId, x.Consumer });
            builder.Property(x => x.MessageId).HasColumnName("message_id");
            builder.Property(x => x.Consumer).HasColumnName("consumer").HasMaxLength(128);
            builder.Property(x => x.ProcessedAt).HasColumnName("processed_at");
        });

        return modelBuilder;
    }
}
