using Ponte.Management.Api.Domain;

namespace Ponte.Management.Api.Infrastructure;

public sealed class ManagementDbContext(DbContextOptions<ManagementDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    public DbSet<WebhookEndpoint> Endpoints => Set<WebhookEndpoint>();

    public DbSet<UsageHourly> Usage => Set<UsageHourly>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(builder =>
        {
            builder.ToTable("tenants");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(120);
            builder.Property(x => x.Plan).HasColumnName("plan").HasMaxLength(32);
            builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<ApiKey>(builder =>
        {
            builder.ToTable("api_keys");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.Property(x => x.TenantId).HasColumnName("tenant_id");
            builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(80);
            builder.Property(x => x.DisplayPrefix).HasColumnName("display_prefix").HasMaxLength(16);
            builder.Property(x => x.KeyHash).HasColumnName("key_hash").HasMaxLength(64).IsUnicode(false);
            builder.Property(x => x.CreatedAt).HasColumnName("created_at");
            builder.Property(x => x.RevokedAt).HasColumnName("revoked_at");
            builder.Ignore(x => x.IsActive);

            // Lookup do Gateway: precisa ser O(log n) e unico.
            builder.HasIndex(x => x.KeyHash).IsUnique();
            builder.HasIndex(x => x.TenantId);
            builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
        });

        modelBuilder.Entity<WebhookEndpoint>(builder =>
        {
            builder.ToTable("endpoints");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.Property(x => x.TenantId).HasColumnName("tenant_id");
            builder.Property(x => x.Url).HasColumnName("url").HasMaxLength(2048);
            builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(256);
            builder.Property(x => x.Secret).HasColumnName("secret").HasMaxLength(128).IsUnicode(false);
            builder.Property(x => x.PreviousSecret).HasColumnName("previous_secret").HasMaxLength(128).IsUnicode(false);
            builder.Property(x => x.PreviousSecretExpiresAt).HasColumnName("previous_secret_expires_at");
            builder.Property(x => x.EventTypes).HasColumnName("event_types");
            builder.Property(x => x.Active).HasColumnName("active");
            builder.Property(x => x.MaxConcurrency).HasColumnName("max_concurrency");
            builder.Property(x => x.Version).HasColumnName("version");
            builder.Property(x => x.Deleted).HasColumnName("deleted");
            builder.Property(x => x.CreatedAt).HasColumnName("created_at");
            builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            builder.Property(x => x.RowVersion).HasColumnName("row_version").IsRowVersion();

            builder.HasIndex(x => new { x.TenantId, x.Deleted });
            builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
        });

        modelBuilder.Entity<UsageHourly>(builder =>
        {
            builder.ToTable("usage_hourly");
            builder.HasKey(x => new { x.TenantId, x.HourBucket, x.EndpointId });
            builder.Property(x => x.TenantId).HasColumnName("tenant_id");
            builder.Property(x => x.EndpointId).HasColumnName("endpoint_id");
            builder.Property(x => x.HourBucket).HasColumnName("hour_bucket").HasColumnType("datetime2(0)");
            builder.Property(x => x.Succeeded).HasColumnName("succeeded");
            builder.Property(x => x.Failed).HasColumnName("failed");
            builder.Property(x => x.Dead).HasColumnName("dead");
            builder.Property(x => x.TotalDurationMs).HasColumnName("total_duration_ms");
        });

        modelBuilder.AddMessagingTables();
    }
}
