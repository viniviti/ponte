namespace Ponte.BuildingBlocks.Hosting;

/// <summary>
/// Paginacao por keyset (cursor). Diferente de OFFSET, o custo nao cresce com a pagina:
/// "WHERE sequence &lt; @cursor ORDER BY sequence DESC LIMIT n" usa o indice direto.
/// </summary>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);

public static class Pagination
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    public static int ClampLimit(int? limit) => Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

    public static long? ParseCursor(string? cursor) =>
        long.TryParse(cursor, out var value) && value > 0 ? value : null;
}
