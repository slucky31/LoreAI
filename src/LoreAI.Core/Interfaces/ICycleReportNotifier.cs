using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

/// <summary>
/// Compte-rendu de fin de cycle (issue #31) : au plus une notification par exécution de
/// <c>UnsortedClassificationJob</c>, uniquement quand au moins un article était à traiter — jamais sur un
/// cycle vide ni sur un échec avant même de savoir s'il y avait quelque chose (voir <see cref="CycleOutcome"/>).
/// </summary>
public interface ICycleReportNotifier
{
    Task NotifyCycleCompletedAsync(CycleRun run, CancellationToken cancellationToken);
}
