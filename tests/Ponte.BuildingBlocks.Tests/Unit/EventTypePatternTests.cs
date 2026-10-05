using Ponte.BuildingBlocks.Routing;

namespace Ponte.BuildingBlocks.Tests.Unit;

public sealed class EventTypePatternTests
{
    [Theory]
    [InlineData("order.paid", "order.paid", true)]
    [InlineData("order.paid", "order.refunded", false)]
    [InlineData("order.*", "order.paid", true)]
    [InlineData("order.*", "order.items.added", false)]
    [InlineData("order.*", "order", false)]
    [InlineData("order.#", "order", true)]
    [InlineData("order.#", "order.items.added", true)]
    [InlineData("*.paid", "invoice.paid", true)]
    [InlineData("order.#.added", "order.added", true)]
    [InlineData("order.#.added", "order.items.added", true)]
    [InlineData("order.#.added", "order.items.removed", false)]
    [InlineData("#", "qualquer.coisa.mesmo", true)]
    public void Matches_segue_a_semantica_de_topicos_do_AMQP(string pattern, string eventType, bool expected)
    {
        EventTypePattern.Matches(pattern, eventType).Should().Be(expected);
    }

    [Fact]
    public void MatchesAny_retorna_true_se_algum_padrao_casar()
    {
        EventTypePattern.MatchesAny(["invoice.#", "order.paid"], "order.paid").Should().BeTrue();
        EventTypePattern.MatchesAny(["invoice.#"], "order.paid").Should().BeFalse();
    }

    [Theory]
    [InlineData("order.paid", true)]
    [InlineData("order.*", true)]
    [InlineData("#", true)]
    [InlineData("billing.invoice-payment_failed", true)]
    [InlineData("", false)]
    [InlineData("Order.Paid", false)]
    [InlineData("order..paid", false)]
    [InlineData("order.*x", false)]
    [InlineData("order paid", false)]
    public void IsValid_valida_a_sintaxe_do_padrao(string pattern, bool expected)
    {
        EventTypePattern.IsValid(pattern).Should().Be(expected);
    }
}
