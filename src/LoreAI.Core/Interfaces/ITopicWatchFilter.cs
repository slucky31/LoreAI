using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

/// <summary>
/// Filtrage LLM de la veille automatique (C4, lot 9, #50) : pertinence pour <paramref name="topic"/>
/// (déjà connu — un candidat vient de la catégorie Miniflux dédiée à ce sujet, pas d'un pool partagé) et
/// nouveauté contre le corpus déjà connu.
/// </summary>
public interface ITopicWatchFilter
{
    Task<WatchEvaluation> EvaluateAsync(
        Item candidate,
        WatchTopic topic,
        RaindropTaxonomy taxonomy,
        IReadOnlyList<LibraryItemSummary> relatedCorpusItems,
        CancellationToken cancellationToken);
}
