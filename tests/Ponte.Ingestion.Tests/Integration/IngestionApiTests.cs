using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ponte.BuildingBlocks.Persistence;
using Ponte.Ingestion.Api.Infrastructure;

namespace Ponte.Ingestion.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class IngestionApiTests(IngestionApiFactory factory) : IClassFixture<IngestionApiFactory>
{
    [Fact]
    public async Task Sem_tenant_retorna_401()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/events", new { eventType = "order.paid", payload = new { id = 1 } });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Evento_valido_retorna_202_e_grava_evento_e_outbox_na_mesma_transacao()
    {
        var client = ClientForNewTenant();

        var response = await client.PostAsJsonAsync("/v1/events", new { eventType = "order.paid", payload = new { id = 1, total = 99.9 } });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var eventId = body.GetProperty("id").GetGuid();
        response.Headers.Location!.ToString().Should().Be($"/v1/events/{eventId}");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IngestionDbContext>();
        (await db.Events.AnyAsync(e => e.Id == eventId)).Should().BeTrue();
        (await db.Set<OutboxMessage>().AnyAsync(m => m.Type == "EventAccepted" && m.Payload.Contains(eventId.ToString())))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Mesma_idempotency_key_devolve_o_mesmo_evento_sem_duplicar()
    {
        var client = ClientForNewTenant();
        var payload = new { eventType = "order.paid", payload = new { id = 7 } };

        var first = await PostAsync(client, payload, "pedido-7");
        var second = await PostAsync(client, payload, "pedido-7");

        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        second.Headers.GetValues("Idempotent-Replayed").Should().Equal("true");
        (await IdOf(second)).Should().Be(await IdOf(first));
    }

    [Fact]
    public async Task Idempotency_key_reutilizada_com_outro_corpo_retorna_409()
    {
        var client = ClientForNewTenant();

        await PostAsync(client, new { eventType = "order.paid", payload = new { id = 1 } }, "chave-x");
        var conflict = await PostAsync(client, new { eventType = "order.paid", payload = new { id = 2 } }, "chave-x");

        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Requisicoes_simultaneas_com_a_mesma_chave_criam_um_unico_evento()
    {
        var client = ClientForNewTenant();
        var payload = new { eventType = "order.paid", payload = new { id = 99 } };

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => PostAsync(client, payload, "corrida")));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Accepted);
        var ids = await Task.WhenAll(responses.Select(IdOf));
        ids.Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task Tipo_de_evento_invalido_retorna_422_com_problem_details()
    {
        var client = ClientForNewTenant();

        var response = await client.PostAsJsonAsync("/v1/events", new { eventType = "Pedido Pago", payload = new { id = 1 } });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("event_type_invalid");
    }

    [Fact]
    public async Task Listagem_pagina_por_cursor_do_mais_novo_para_o_mais_antigo()
    {
        var client = ClientForNewTenant();
        foreach (var type in new[] { "a.one", "a.two", "a.three" })
        {
            await client.PostAsJsonAsync("/v1/events", new { eventType = type, payload = new { } });
        }

        var page1 = await client.GetFromJsonAsync<JsonElement>("/v1/events?limit=2");
        var items1 = page1.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("eventType").GetString()).ToList();
        items1.Should().Equal("a.three", "a.two");

        var cursor = page1.GetProperty("nextCursor").GetString();
        var page2 = await client.GetFromJsonAsync<JsonElement>($"/v1/events?limit=2&cursor={cursor}");
        page2.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("eventType").GetString()).Should().Equal("a.one");
        page2.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Tenant_nao_enxerga_eventos_de_outro_tenant()
    {
        var owner = ClientForNewTenant();
        var response = await owner.PostAsJsonAsync("/v1/events", new { eventType = "order.paid", payload = new { id = 1 } });
        var eventId = await IdOf(response);

        var intruder = ClientForNewTenant();
        (await intruder.GetAsync($"/v1/events/{eventId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/v1/events/{eventId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private HttpClient ClientForNewTenant()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", Guid.NewGuid().ToString());
        return client;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, object body, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/events") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return client.SendAsync(request);
    }

    private static async Task<Guid> IdOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
}
