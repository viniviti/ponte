using System.Collections.Concurrent;
using Ponte.BuildingBlocks.Security;

// Receptor de webhooks para demonstracao e testes de carga. Simula um cliente real:
//   ?failRate=0.3   responde 503 em 30% das chamadas
//   ?latencyMs=500  demora para responder
//   ?status=410     forca um status especifico
// Tambem mostra o lado do cliente: valida a assinatura Standard Webhooks e deduplica
// pelo webhook-id (entrega e at-least-once, entao o receptor precisa ser idempotente).

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var received = new ConcurrentQueue<ReceivedWebhook>();
var seenIds = new ConcurrentDictionary<string, byte>();
var secret = app.Configuration["WEBHOOK_SECRET"];

app.MapPost("/webhooks/{name}", async (string name, HttpRequest request, double? failRate, int? latencyMs, int? status) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();

    if (latencyMs is > 0)
    {
        await Task.Delay(Math.Min(latencyMs.Value, 30_000));
    }

    var id = request.Headers[StandardWebhook.IdHeader].ToString();
    var timestampHeader = request.Headers[StandardWebhook.TimestampHeader].ToString();
    var signature = request.Headers[StandardWebhook.SignatureHeader].ToString();

    bool? signatureValid = null;
    if (!string.IsNullOrEmpty(secret) && long.TryParse(timestampHeader, out var timestamp))
    {
        signatureValid = StandardWebhook.Verify(secret, id, timestamp, body, signature, DateTimeOffset.UtcNow);
    }

    var duplicate = !string.IsNullOrEmpty(id) && !seenIds.TryAdd(id + ":" + name, 0);
    var responseStatus = status ?? (Random.Shared.NextDouble() < (failRate ?? 0) ? 503 : 200);

    received.Enqueue(new ReceivedWebhook(name, id, request.Headers["ponte-event-type"].ToString(),
        request.Headers["ponte-attempt"].ToString(), responseStatus, duplicate, signatureValid, DateTimeOffset.UtcNow));

    while (received.Count > 500 && received.TryDequeue(out _))
    {
    }

    // Sucesso de um evento ja processado: responde 200 sem reprocessar (idempotencia).
    return responseStatus is >= 200 and < 300
        ? Results.Ok(new { received = true, duplicate })
        : Results.Problem(statusCode: responseStatus, title: "falha simulada");
});

app.MapGet("/received", () => received.Reverse().Take(100));
app.MapGet("/health/live", () => Results.Ok("ok"));

app.Run();

internal sealed record ReceivedWebhook(
    string Endpoint,
    string WebhookId,
    string EventType,
    string Attempt,
    int Status,
    bool Duplicate,
    bool? SignatureValid,
    DateTimeOffset ReceivedAt);
