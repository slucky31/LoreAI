using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

/// <summary>Filtrage LLM de la veille automatique (C4, lot 9, #50) : pertinence contre les sujets suivis, nouveauté contre le corpus déjà connu.</summary>
public interface ITopicWatchFilter
{
    Task<WatchEvaluation> EvaluateAsync(
        Item candidate,
        IReadOnlyList<WatchTopic> topics,
        IReadOnlyList<LibraryItemSummary> relatedCorpusItems,
        CancellationToken cancellationToken);
}
