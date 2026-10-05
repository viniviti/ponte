using System.Text.RegularExpressions;

namespace Ponte.BuildingBlocks.Routing;

/// <summary>
/// Specification de assinatura de eventos, com a mesma semantica de topicos do AMQP:
///   order.paid    casa exatamente
///   order.*       casa exatamente UM segmento (order.paid, order.refunded)
///   order.#       casa zero ou mais segmentos (order, order.paid, order.items.added)
///   #             casa tudo
/// </summary>
public static partial class EventTypePattern
{
    public const int MaxPatternsPerEndpoint = 50;

    [GeneratedRegex(@"^(\*|#|[a-z0-9_-]+)(\.(\*|#|[a-z0-9_-]+))*$", RegexOptions.CultureInvariant)]
    private static partial Regex PatternSyntax();

    public static bool IsValid(string pattern) => pattern.Length <= 128 && PatternSyntax().IsMatch(pattern);

    public static bool MatchesAny(IEnumerable<string> patterns, string eventType) =>
        patterns.Any(pattern => Matches(pattern, eventType));

    public static bool Matches(string pattern, string eventType)
    {
        if (pattern == "#")
        {
            return true;
        }

        return Match(pattern.Split('.'), 0, eventType.Split('.'), 0);
    }

    private static bool Match(string[] pattern, int p, string[] words, int w)
    {
        while (true)
        {
            if (p == pattern.Length)
            {
                return w == words.Length;
            }

            if (pattern[p] == "#")
            {
                if (p == pattern.Length - 1)
                {
                    return true;
                }

                // "#" pode consumir de 0 a N segmentos: testa cada possibilidade.
                for (var skip = w; skip <= words.Length; skip++)
                {
                    if (Match(pattern, p + 1, words, skip))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (w == words.Length)
            {
                return false;
            }

            if (pattern[p] != "*" && !string.Equals(pattern[p], words[w], StringComparison.Ordinal))
            {
                return false;
            }

            p++;
            w++;
        }
    }
}
