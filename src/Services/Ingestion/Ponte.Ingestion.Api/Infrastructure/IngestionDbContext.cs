using Ponte.Ingestion.Api.Domain;

namespace Ponte.Ingestion.Api.Infrastructure;

public sealed class IngestionDbContext(DbContextOptions<IngestionDbContext> options) : DbContext(options)
{
    public DbSet<IngestedEvent> Events => Set<IngestedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IngestedEvent>(builder =>
        {
            builder.ToTable("events");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.Property(x => x.Sequence).HasColumnName("sequence").UseIdentityByDefaultColumn();
            builder.Property(x => x.TenantId).HasColumnName("tenant_id");
            builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(128).IsRequired();

            // jsonb: permite consultar o conteudo do evento (GIN index) se um dia precisarmos.
            builder.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
            builder.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64).IsFixedLength().IsRequired();
            builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128);
            builder.Property(x => x.ReceivedAt).HasColumnName("received_at");

            builder.HasIndex(x => x.Sequence).IsUnique();

            // Listagem do console: tenant + mais recentes primeiro, sem sort em memoria.
            builder.HasIndex(x => new { x.TenantId, x.Sequence })
                .HasDatabaseName("ix_events_tenant_sequence")
                .IsDescending(false, true);

            // A unicidade garante idempotencia mesmo com duas requisicoes simultaneas.
            builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey })
                .HasDatabaseName("ux_events_tenant_idempotency")
                .IsUnique()
                .HasFilter("idempotency_key IS NOT NULL");
        });

        modelBuilder.AddMessagingTables();
    }
}
