using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ponte.BuildingBlocks.Messaging;
using Ponte.BuildingBlocks.Persistence;
using Ponte.BuildingBlocks.Security;
using Ponte.Contracts;
using Ponte.Management.Api.Application;
using Ponte.Management.Api.Domain;
using Ponte.Management.Api.Infrastructure;

namespace Ponte.Management.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class ManagementSqlServerTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task Criar_endpoint_grava_entidade_e_EndpointUpserted_no_outbox()
    {
        var tenantId = await CreateTenantAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<EndpointService>();
        var result = await service.CreateAsync(
            tenantId, new EndpointInput("https://erp.cliente.com/webhooks", "ERP", ["order.*"], 5, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var db = scope.ServiceProvider.GetRequiredService<ManagementDbContext>();
        var endpointId = result.Value.Id.ToString();
        var outbox = await db.Set<OutboxMessage>().AsNoTracking().SingleAsync(m => m.Payload.Contains(endpointId));

        outbox.Type.Should().Be(nameof(EndpointUpserted));
        outbox.RoutingKey.Should().Be("management.endpoint.upserted");
    }

    [Theory]
    [InlineData("http://erp.cliente.com/webhooks", "url_invalid")]
    [InlineData("https://192.168.0.10/webhooks", "url_invalid")]
    public async Task Rejeita_urls_inseguras_ou_de_rede_privada(string url, string expectedCode)
    {
        var tenantId = await CreateTenantAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<EndpointService>()
            .CreateAsync(tenantId, new EndpointInput(url, null, ["#"], null, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(expectedCode);
    }

    [Fact]
    public async Task Rejeita_padrao_de_evento_invalido()
    {
        var tenantId = await CreateTenantAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<EndpointService>()
            .CreateAsync(tenantId, new EndpointInput("https://x.com/hook", null, ["order..paid"], null, null), CancellationToken.None);

        result.Error!.Code.Should().Be("event_type_pattern_invalid");
    }

    [Fact]
    public async Task Metering_agrega_por_hora_com_MERGE_e_conta_cada_mensagem_uma_unica_vez()
    {
        var tenantId = await CreateTenantAsync();
        var endpointId = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 10, 5, 14, 10, 0, TimeSpan.Zero);

        var success = Attempted(tenantId, endpointId, DeliveryOutcomes.Succeeded, at, 120);
        var retrying = Attempted(tenantId, endpointId, DeliveryOutcomes.Retrying, at.AddMinutes(20), 80);
        var dead = Attempted(tenantId, endpointId, DeliveryOutcomes.Dead, at.AddMinutes(40), 100);

        await HandleAsync(success);
        await HandleAsync(retrying);
        await HandleAsync(dead);
        await HandleAsync(success); // redelivery do broker: nao pode contar de novo

        await using var scope = fixture.Services.CreateAsyncScope();
        var usage = await scope.ServiceProvider.GetRequiredService<ManagementDbContext>()
            .Usage.AsNoTracking().SingleAsync(u => u.TenantId == tenantId);

        usage.HourBucket.Should().Be(new DateTime(2026, 10, 5, 14, 0, 0));
        usage.Succeeded.Should().Be(1);
        usage.Failed.Should().Be(2);
        usage.Dead.Should().Be(1);
        usage.TotalDurationMs.Should().Be(300);
    }

    [Fact]
    public async Task Metering_concorrente_nao_perde_incrementos()
    {
        var tenantId = await CreateTenantAsync();
        var endpointId = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

        await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => HandleAsync(Attempted(tenantId, endpointId, DeliveryOutcomes.Succeeded, at, 10))));

        await using var scope = fixture.Services.CreateAsyncScope();
        var usage = await scope.ServiceProvider.GetRequiredService<ManagementDbContext>()
            .Usage.AsNoTracking().SingleAsync(u => u.TenantId == tenantId);

        usage.Succeeded.Should().Be(20);
    }

    [Fact]
    public async Task Api_key_resolve_o_tenant_ate_ser_revogada()
    {
        var tenantId = await CreateTenantAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
        var created = (await service.CreateAsync(tenantId, "CI", CancellationToken.None)).Value;

        (await service.ResolveTenantAsync(ApiKeys.Hash(created.Key), CancellationToken.None)).Should().Be(tenantId);

        await service.RevokeAsync(tenantId, created.Id, CancellationToken.None);
        (await service.ResolveTenantAsync(ApiKeys.Hash(created.Key), CancellationToken.None)).Should().BeNull();
    }

    private async Task<Guid> CreateTenantAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ManagementDbContext>();
        var tenant = Tenant.Create(Guid.NewGuid(), "Teste", "pro", DateTimeOffset.UtcNow);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    private async Task HandleAsync(DeliveryAttempted message)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MeteringHandler>()
            .HandleAsync(message, new MessageContext(message.Id, "delivery.attempted", false), CancellationToken.None);
    }

    private static DeliveryAttempted Attempted(Guid tenantId, Guid endpointId, string outcome, DateTimeOffset at, long durationMs) =>
        new(Guid.NewGuid(), Guid.NewGuid(), endpointId, tenantId, "order.paid", 1, outcome, 200, durationMs, at, null);
}
