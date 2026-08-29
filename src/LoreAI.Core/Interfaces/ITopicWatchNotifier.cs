using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

/// <summary>Alerte Discord pour une entrée de veille jugée pertinente et nouvelle (C4, lot 9, #50).</summary>
public interface ITopicWatchNotifier
{
    Task NotifyAsync(Item candidate, WatchEvaluation evaluation, CancellationToken cancellationToken);
}
