using LoreAI.Core.Enums;
using LoreAI.Core.Models;

namespace LoreAI.Core.Services;

/// <summary>
/// Décide si le worker est en vie, à partir des derniers <see cref="CycleRun"/> connus. Pure, testable sans
/// I/O — même esprit que <c>ClassificationNoteBuilder</c>/<c>DigestMessageBuilder</c>.
/// </summary>
public static class HealthEvaluator
{
    /// <summary>3 échecs consécutifs, pas 1 : une panne ponctuelle de l'API Raindrop ne doit pas déclencher l'alarme.</summary>
    private const int ConsecutiveFailuresThreshold = 3;

    public static bool IsHealthy(IReadOnlyList<CycleRun> recentRuns, DateTimeOffset now, TimeSpan maxCycleAge)
    {
        if (recentRuns.Count == 0)
        {
            return false;
        }

        var freshEnough = recentRuns[0].CompletedUtc >= now - maxCycleAge;
        var allRecentFailed = recentRuns.Count >= ConsecutiveFailuresThreshold
            && recentRuns.Take(ConsecutiveFailuresThreshold).All(r => r.Outcome == CycleOutcome.Failed);

        return freshEnough && !allRecentFailed;
    }
}
