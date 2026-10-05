namespace Ponte.Delivery.Api.Domain;

/// <summary>
/// Copia local (somente leitura) do endpoint, alimentada por eventos do Management.
/// Database-per-service: o Delivery nunca consulta o SQL Server do Management, entao
/// continua entregando mesmo se o Management estiver fora do ar.
/// </summary>
public sealed class EndpointReplica
{
    private EndpointReplica()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Url { get; private set; } = string.Empty;

    public string Secret { get; private set; } = string.Empty;

    /// <summary>Segredo anterior, ainda valido durante a janela de rotacao.</summary>
    public string? PreviousSecret { get; private set; }

    public DateTimeOffset? PreviousSecretExpiresAt { get; private set; }

    public string[] EventTypes { get; private set; } = [];

    public bool Active { get; private set; }

    public int MaxConcurrency { get; private set; }

    /// <summary>Versao vinda do Management: mensagens antigas que chegam atrasadas sao ignoradas.</summary>
    public long SourceVersion { get; private set; }

    public bool Deleted { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint RowVersion { get; private set; }

    public static EndpointReplica From(EndpointUpserted message, DateTimeOffset now)
    {
        var replica = new EndpointReplica { Id = message.EndpointId, TenantId = message.TenantId };
        replica.Apply(message, now);
        return replica;
    }

    /// <summary>Aplica o estado se for mais novo. Retorna false para mensagem obsoleta.</summary>
    public bool Apply(EndpointUpserted message, DateTimeOffset now)
    {
        if (SourceVersion != 0 && message.Version <= SourceVersion)
        {
            return false;
        }

        Url = message.Url;
        Secret = message.Secret;
        PreviousSecret = message.PreviousSecret;
        PreviousSecretExpiresAt = message.PreviousSecretExpiresAt;
        EventTypes = message.EventTypes.ToArray();
        Active = message.Active;
        MaxConcurrency = Math.Max(1, message.MaxConcurrency);
        SourceVersion = message.Version;
        Deleted = false;
        UpdatedAt = now;
        return true;
    }

    /// <summary>Segredos que devem assinar a entrega agora (o atual e, se ainda valido, o anterior).</summary>
    public IReadOnlyList<string> SigningSecrets(DateTimeOffset now) =>
        PreviousSecret is not null && PreviousSecretExpiresAt > now
            ? new[] { Secret, PreviousSecret }
            : new[] { Secret };

    /// <summary>Tombstone: um Upserted atrasado nao "ressuscita" um endpoint removido.</summary>
    public bool MarkDeleted(long version, DateTimeOffset now)
    {
        if (version <= SourceVersion)
        {
            return false;
        }

        Deleted = true;
        Active = false;
        SourceVersion = version;
        UpdatedAt = now;
        return true;
    }
}
