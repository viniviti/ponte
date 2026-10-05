using System.Net;
using Microsoft.Extensions.Options;
using Ponte.BuildingBlocks.Security;
using Ponte.Delivery.Api.Infrastructure.Http;

namespace Ponte.Delivery.Tests.Unit;

public sealed class WebhookHttpSenderTests
{
    private static readonly string Secret = StandardWebhook.GenerateSecret();
    private const string Body = """{"type":"order.paid","timestamp":"2026-10-05T12:00:00.000Z","data":{"id":1}}""";

    [Fact]
    public async Task Envia_POST_com_headers_Standard_Webhooks_verificaveis_pelo_cliente()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var sender = CreateSender(handler);
        var messageId = Guid.NewGuid();

        var result = await sender.SendAsync(Request([Secret], messageId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var request = handler.LastRequest!;
        request.Method.Should().Be(HttpMethod.Post);
        handler.LastBody.Should().Be(Body);

        var id = Header(request, StandardWebhook.IdHeader);
        id.Should().Be($"msg_{messageId:N}");

        var timestamp = long.Parse(Header(request, StandardWebhook.TimestampHeader));
        var signature = Header(request, StandardWebhook.SignatureHeader);
        StandardWebhook.Verify(Secret, id, timestamp, Body, signature, DateTimeOffset.UtcNow).Should().BeTrue();
        Header(request, WebhookHttpSender.AttemptHeader).Should().Be("1");
    }

    [Fact]
    public async Task Durante_rotacao_envia_uma_assinatura_por_segredo()
    {
        var oldSecret = StandardWebhook.GenerateSecret();
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        await CreateSender(handler).SendAsync(Request([Secret, oldSecret]), CancellationToken.None);

        var request = handler.LastRequest!;
        var id = Header(request, StandardWebhook.IdHeader);
        var timestamp = long.Parse(Header(request, StandardWebhook.TimestampHeader));
        var signature = Header(request, StandardWebhook.SignatureHeader);

        signature.Split(' ').Should().HaveCount(2);
        StandardWebhook.Verify(oldSecret, id, timestamp, Body, signature, DateTimeOffset.UtcNow).Should().BeTrue();
        StandardWebhook.Verify(Secret, id, timestamp, Body, signature, DateTimeOffset.UtcNow).Should().BeTrue();
    }

    [Fact]
    public async Task Timeout_vira_resultado_de_falha_e_nao_excecao()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var result = await CreateSender(handler, TimeSpan.FromMilliseconds(100)).SendAsync(Request([Secret]), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().BeNull();
        result.Error.Should().StartWith("Timeout");
        result.IndicatesUnhealthyEndpoint.Should().BeTrue();
    }

    [Fact]
    public async Task Erro_de_conexao_vira_resultado_de_falha()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("Connection refused"));

        var result = await CreateSender(handler).SendAsync(Request([Secret]), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Connection refused");
    }

    [Fact]
    public async Task Le_Retry_After_e_trecho_da_resposta()
    {
        var handler = new StubHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("slow down") };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
            return Task.FromResult(response);
        });

        var result = await CreateSender(handler).SendAsync(Request([Secret]), CancellationToken.None);

        result.StatusCode.Should().Be(429);
        result.RetryAfter.Should().Be(TimeSpan.FromSeconds(120));
        result.ResponseSnippet.Should().Be("slow down");
    }

    private static WebhookHttpSender CreateSender(StubHandler handler, TimeSpan? timeout = null) => new(
        new HttpClient(handler),
        TimeProvider.System,
        Options.Create(new WebhookSenderOptions { Timeout = timeout ?? TimeSpan.FromSeconds(5) }));

    private static WebhookRequest Request(IReadOnlyList<string> secrets, Guid? messageId = null) =>
        new(new Uri("https://cliente.example.com/hook"), secrets, messageId ?? Guid.NewGuid(), "order.paid", Body, 1);

    private static string Header(HttpRequestMessage request, string name) => request.Headers.GetValues(name).Single();

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return await responder(request, cancellationToken);
        }
    }
}
