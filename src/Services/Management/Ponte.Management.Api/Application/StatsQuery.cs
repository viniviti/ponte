using Ponte.Management.Api.Infrastructure;

namespace Ponte.Management.Api.Application;

public sealed record HourlyPoint(DateTime Hour, long Succeeded, long Failed, long Dead);

public sealed record StatsOverview(
    int Hours,
    long Attempts,
    long Succeeded,
    long Failed,
    long Dead,
    double SuccessRate,
    double AverageLatencyMs,
    IReadOnlyList<HourlyPoint> Series);

public sealed record EndpointStats(Guid EndpointId, long Succeeded, long Failed, long Dead, double AverageLatencyMs);

public sealed class StatsQuery(ManagementDbContext dbContext, TimeProvider clock)
{
    public async Task<StatsOverview> OverviewAsync(Guid tenantId, int hours, CancellationToken cancellationToken)
    {
        hours = Math.Clamp(hours, 1, 24 * 30);
        var now = clock.GetUtcNow().UtcDateTime;
        var currentHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        var from = currentHour.AddHours(-(hours - 1));

        var rows = await dbContext.Usage
            .AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.HourBucket >= from)
            .GroupBy(u => u.HourBucket)
            .Select(g => new
            {
                Hour = g.Key,
                Succeeded = g.Sum(x => x.Succeeded),
                Failed = g.Sum(x => x.Failed),
                Dead = g.Sum(x => x.Dead),
                Duration = g.Sum(x => x.TotalDurationMs),
            })
            .ToListAsync(cancellationToken);

        var byHour = rows.ToDictionary(r => DateTime.SpecifyKind(r.Hour, DateTimeKind.Utc));

        // Serie completa (horas sem trafego aparecem com zero, o grafico nao "pula").
        var series = Enumerable.Range(0, hours)
            .Select(i => from.AddHours(i))
            .Select(h => byHour.TryGetValue(h, out var r)
                ? new HourlyPoint(h, r.Succeeded, r.Failed, r.Dead)
                : new HourlyPoint(h, 0, 0, 0))
            .ToList();

        var succeeded = rows.Sum(r => r.Succeeded);
        var failed = rows.Sum(r => r.Failed);
        var attempts = succeeded + failed;
        var duration = rows.Sum(r => r.Duration);

        return new StatsOverview(
            hours,
            attempts,
            succeeded,
            failed,
            rows.Sum(r => r.Dead),
            attempts == 0 ? 1 : Math.Round((double)succeeded / attempts, 4),
            attempts == 0 ? 0 : Math.Round((double)duration / attempts, 1),
            series);
    }

    public async Task<IReadOnlyDictionary<Guid, EndpointStats>> PerEndpointAsync(Guid tenantId, int hours, CancellationToken cancellationToken)
    {
        var from = clock.GetUtcNow().UtcDateTime.AddHours(-hours);

        var rows = await dbContext.Usage
            .AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.HourBucket >= from)
            .GroupBy(u => u.EndpointId)
            .Select(g => new
            {
                EndpointId = g.Key,
                Succeeded = g.Sum(x => x.Succeeded),
                Failed = g.Sum(x => x.Failed),
                Dead = g.Sum(x => x.Dead),
                Duration = g.Sum(x => x.TotalDurationMs),
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            r => r.EndpointId,
            r =>
            {
                var attempts = r.Succeeded + r.Failed;
                return new EndpointStats(r.EndpointId, r.Succeeded, r.Failed, r.Dead, attempts == 0 ? 0 : Math.Round((double)r.Duration / attempts, 1));
            });
    }
}
