using Microsoft.Extensions.Options;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Services;
using LoreAI.Worker.Options;

namespace LoreAI.Worker;

/// <summary>
/// Sonde `--health-check` (#35) : un second processus .NET, démarré par Docker toutes les 5 min sur une
/// image chiselée sans shell ni curl (cf. roadmap, « Ce que O2 peut et ne peut pas faire »). Réutilise le
/// même <see cref="IServiceProvider"/> que le worker normal — construit par <c>Program.cs</c> sans jamais
/// appeler <c>host.Run()</c>, donc sans déclencher la validation des options non liées à Postgres/CycleRuns.
/// </summary>
public static class HealthCheckMode
{
    private const int RecentRunsToInspect = 3;

    public static async Task<bool> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var cycleRunRepository = services.GetRequiredService<ICycleRunRepository>();
        var workerOptions = services.GetRequiredService<IOptions<WorkerOptions>>().Value;

        var recentRuns = await cycleRunRepository.GetRecentAsync(RecentRunsToInspect, cancellationToken);

        return HealthEvaluator.IsHealthy(recentRuns, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(workerOptions.HealthMaxCycleAgeMinutes));
    }
}
