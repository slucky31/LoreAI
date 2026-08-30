using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

/// <summary>Sujets de veille persistés (C4, lot 9, #50) — créés par la commande <c>--add-watch-topic</c>, lus par <c>TopicWatchJob</c>.</summary>
public interface IWatchTopicRepository
{
    Task<IReadOnlyList<WatchTopic>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Retourne l'id généré par la base.</summary>
    Task<long> AddAsync(WatchTopic topic, CancellationToken cancellationToken);

    /// <summary>Avance le curseur d'ingestion d'un sujet, indépendamment du sort de chaque candidat traité (même raisonnement que les autres ingesteurs).</summary>
    Task UpdateCursorAsync(long topicId, string lastMinifluxEntryId, CancellationToken cancellationToken);
}
