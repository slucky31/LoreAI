using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

/// <summary>
/// Résumé Discord d'une exécution de la veille (C4, lot 9, #50) — un seul message par run, remplace la
/// notification détaillée par article (chaque match crée désormais directement un raindrop, cf.
/// <c>IRaindropClient.CreateRaindropAsync</c>).
/// </summary>
public interface IWatchDigestNotifier
{
    Task NotifyAsync(WatchRunSummary summary, CancellationToken cancellationToken);
}
