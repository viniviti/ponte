using Ponte.Delivery.Api.Domain;

namespace Ponte.Delivery.Api.Infrastructure;

public sealed class DeliveryDbContext(DbContextOptions<DeliveryDbContext> options) : DbContext(options)
{
    public DbSet<WebhookDelivery> Deliveries => Set<WebhookDelivery>();

    public DbSet<DeliveryAttempt> Attempts => Set<DeliveryAttempt>();

    public DbSet<EndpointReplica> Endpoints => Set<EndpointReplica>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WebhookDelivery>(builder =>
        {
            builder.ToTable("deliveries");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.Property(x => x.Sequence).HasColumnName("sequence").UseIdentityByDefaultColumn();
            builder.Property(x => x.TenantId).HasColumnName("tenant_id");
            builder.Property(x => x.EventId).HasColumnName("event_id");
            builder.Property(x => x.EndpointId).HasColumnName("endpoint_id");
            builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(128);

            // text (e nao jsonb): precisamos dos bytes EXATOS que foram assinados.
            builder.Property(x => x.Body).HasColumnName("body").HasColumnType("text");
            builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16);
            builder.Property(x => x.AttemptCount).HasColumnName("attempt_count");
            builder.Property(x => x.CycleAttempts).HasColumnName("cycle_attempts");
            builder.Property(x => x.LastStatusCode).HasColumnName("last_status_code");
            builder.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
            builder.Property(x => x.LeaseUntil).HasColumnName("lease_until");
            builder.Property(x => x.CreatedAt).HasColumnName("created_at");
            builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
            builder.Property(x => x.Version).IsRowVersion();

            builder.HasMany(x => x.Attempts)
                .WithOne()
                .HasForeignKey(x => x.DeliveryId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(x => x.Sequence).IsUnique();
            builder.HasIndex(x => new { x.TenantId, x.Sequence }).IsDescending(false, true);
            builder.HasIndex(x => new { x.TenantId, x.Status, x.Sequence }).IsDescending(false, false, true);

            // Defesa em profundidade: mesmo que o Inbox falhe, o banco impede fan-out duplicado.
            builder.HasIndex(x => new { x.EventId, x.EndpointId }).IsUnique();
        });

        modelBuilder.Entity<DeliveryAttempt>(builder =>
        {
            builder.ToTable("delivery_attempts");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.Property(x => x.DeliveryId).HasColumnName("delivery_id");
            builder.Property(x => x.AttemptNumber).HasColumnName("attempt_number");
            builder.Property(x => x.StatusCode).HasColumnName("status_code");
            builder.Property(x => x.Succeeded).HasColumnName("succeeded");
            builder.Property(x => x.DurationMs).HasColumnName("duration_ms");
            builder.Property(x => x.Error).HasColumnName("error").HasMaxLength(500);
            builder.Property(x => x.ResponseSnippet).HasColumnName("response_snippet").HasMaxLength(1024);
            builder.Property(x => x.AttemptedAt).HasColumnName("attempted_at");
            builder.HasIndex(x => new { x.DeliveryId, x.AttemptNumber });
        });

        modelBuilder.Entity<EndpointReplica>(builder =>
        {
            builder.ToTable("endpoint_replicas");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.Property(x => x.TenantId).HasColumnName("tenant_id");
            builder.Property(x => x.Url).HasColumnName("url").HasMaxLength(2048);
            builder.Property(x => x.Secret).HasColumnName("secret").HasMaxLength(128);
            builder.Property(x => x.PreviousSecret).HasColumnName("previous_secret").HasMaxLength(128);
            builder.Property(x => x.PreviousSecretExpiresAt).HasColumnName("previous_secret_expires_at");
            builder.Property(x => x.EventTypes).HasColumnName("event_types");
            builder.Property(x => x.Active).HasColumnName("active");
            builder.Property(x => x.MaxConcurrency).HasColumnName("max_concurrency");
            builder.Property(x => x.SourceVersion).HasColumnName("source_version");
            builder.Property(x => x.Deleted).HasColumnName("deleted");
            builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            builder.Property(x => x.RowVersion).IsRowVersion();
            builder.HasIndex(x => x.TenantId);
        });

        modelBuilder.AddMessagingTables();
    }
}
