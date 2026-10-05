using System.Text.Json;
using Ponte.Delivery.Api.Domain;

namespace Ponte.Delivery.Tests.Unit;

public sealed class WebhookDeliveryTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(60);

    [Fact]
    public void Create_monta_envelope_standard_webhooks_com_type_timestamp_e_data()
    {
        var delivery = TestData.NewDelivery("order.paid", """{"id":42,"total":99.9}""");

        using var body = JsonDocument.Parse(delivery.Body);
        body.RootElement.GetProperty("type").GetString().Should().Be("order.paid");
        body.RootElement.GetProperty("timestamp").GetString().Should().Be("2026-10-05T12:00:00.000Z");
        body.RootElement.GetProperty("data").GetProperty("id").GetInt32().Should().Be(42);
        delivery.Status.Should().Be(DeliveryStatus.Pending);
    }

    [Fact]
    public void TryClaim_reivindica_entrega_pendente()
    {
        var delivery = TestData.NewDelivery();

        delivery.TryClaim(TestData.Now, Lease).Should().Be(ClaimResult.Claimed);
        delivery.Status.Should().Be(DeliveryStatus.InFlight);
        delivery.LeaseUntil.Should().Be(TestData.Now + Lease);
    }

    [Fact]
    public void TryClaim_respeita_lease_de_outra_instancia_ate_expirar()
    {
        var delivery = TestData.NewDelivery();
        delivery.TryClaim(TestData.Now, Lease);

        delivery.TryClaim(TestData.Now.AddSeconds(30), Lease).Should().Be(ClaimResult.InFlightElsewhere);
        delivery.TryClaim(TestData.Now.AddSeconds(61), Lease).Should().Be(ClaimResult.Claimed);
    }

    [Fact]
    public void TryClaim_ignora_job_antigo_que_chega_antes_da_hora()
    {
        var delivery = TestData.NewDelivery();
        delivery.TryClaim(TestData.Now, Lease);
        delivery.RecordAttempt(TestData.Status(503), TestData.RetryPolicy, TestData.Now); // agenda para +5s

        delivery.TryClaim(TestData.Now.AddSeconds(1), Lease).Should().Be(ClaimResult.NotDueYet);
        delivery.TryClaim(TestData.Now.AddSeconds(5), Lease).Should().Be(ClaimResult.Claimed);
    }

    [Fact]
    public void RecordAttempt_com_2xx_conclui_a_entrega()
    {
        var delivery = Claimed();

        var attempt = delivery.RecordAttempt(TestData.Status(204), TestData.RetryPolicy, TestData.Now);

        delivery.Status.Should().Be(DeliveryStatus.Succeeded);
        delivery.CompletedAt.Should().Be(TestData.Now);
        delivery.IsTerminal.Should().BeTrue();
        attempt.Succeeded.Should().BeTrue();
        attempt.AttemptNumber.Should().Be(1);
    }

    [Fact]
    public void RecordAttempt_com_5xx_agenda_retry_no_primeiro_degrau()
    {
        var delivery = Claimed();

        delivery.RecordAttempt(TestData.Status(503), TestData.RetryPolicy, TestData.Now);

        delivery.Status.Should().Be(DeliveryStatus.Scheduled);
        delivery.NextAttemptAt.Should().Be(TestData.Now.AddSeconds(5));
        delivery.LeaseUntil.Should().BeNull();
    }

    [Fact]
    public void RecordAttempt_com_4xx_desiste_imediatamente()
    {
        var delivery = Claimed();

        delivery.RecordAttempt(TestData.Status(422), TestData.RetryPolicy, TestData.Now);

        delivery.Status.Should().Be(DeliveryStatus.Dead);
        delivery.OutcomeName.Should().Be("dead");
    }

    [Fact]
    public void Entrega_morre_depois_de_esgotar_todos_os_degraus()
    {
        var delivery = TestData.NewDelivery();
        var now = TestData.Now;

        for (var i = 0; i < 7; i++)
        {
            delivery.TryClaim(now, Lease).Should().Be(ClaimResult.Claimed);
            delivery.RecordAttempt(TestData.NetworkError(), TestData.RetryPolicy, now);
            now = delivery.NextAttemptAt ?? now;
        }

        delivery.Status.Should().Be(DeliveryStatus.Dead);
        delivery.AttemptCount.Should().Be(7);
        delivery.Attempts.Should().HaveCount(7);
    }

    [Fact]
    public void RecordAttempt_sem_reivindicar_e_erro_de_programacao()
    {
        var delivery = TestData.NewDelivery();

        var act = () => delivery.RecordAttempt(TestData.Status(200), TestData.RetryPolicy, TestData.Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Defer_adia_sem_consumir_tentativa()
    {
        var delivery = Claimed();

        delivery.Defer(TestData.Now.AddSeconds(30), TestData.Now);

        delivery.Status.Should().Be(DeliveryStatus.Scheduled);
        delivery.AttemptCount.Should().Be(0);
        delivery.NextAttemptAt.Should().Be(TestData.Now.AddSeconds(30));
    }

    [Fact]
    public void Replay_reabre_entrega_morta_e_zera_o_ciclo_de_retry()
    {
        var delivery = Claimed();
        delivery.RecordAttempt(TestData.Status(400), TestData.RetryPolicy, TestData.Now);

        var error = delivery.Replay(TestData.Now.AddHours(1));

        error.Should().BeNull();
        delivery.Status.Should().Be(DeliveryStatus.Pending);
        delivery.CycleAttempts.Should().Be(0);
        delivery.AttemptCount.Should().Be(1, "o historico continua numerado");
    }

    [Fact]
    public void Replay_de_entrega_bem_sucedida_e_recusado()
    {
        var delivery = Claimed();
        delivery.RecordAttempt(TestData.Status(200), TestData.RetryPolicy, TestData.Now);

        delivery.Replay(TestData.Now)!.Code.Should().Be("delivery_not_replayable");
    }

    private static WebhookDelivery Claimed()
    {
        var delivery = TestData.NewDelivery();
        delivery.TryClaim(TestData.Now, Lease);
        return delivery;
    }
}
