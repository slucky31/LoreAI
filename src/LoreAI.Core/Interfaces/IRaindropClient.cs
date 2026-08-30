using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

/// <summary>
/// Première implémentation d'<see cref="ISourceIngester"/> (ADR 0012), enrichie des deux membres qui
/// restent strictement propres à Raindrop : la taxonomie apprise (collections/tags) et le write-back.
/// </summary>
public interface IRaindropClient : ISourceIngester
{
    /// <summary>Apprend les collections et tags réellement en place dans le compte de l'utilisateur.</summary>
    Task<RaindropTaxonomy> GetTaxonomyAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Met à jour un raindrop existant : tags et note (fusionnés côté appelant, jamais écrasés ici),
    /// et déplacement optionnel vers une autre collection si <paramref name="collectionId"/> est fourni.
    /// </summary>
    Task UpdateRaindropAsync(long raindropId, IReadOnlyCollection<string> tags, string note, long? collectionId, CancellationToken cancellationToken);

    /// <summary>
    /// Une page de toute la bibliothèque (collection 0, hors corbeille — lot 1, #42), pas les seuls nouveaux
    /// items depuis un curseur : hors du contrat <see cref="ISourceIngester"/>, propre à ce balayage complet.
    /// Une liste vide signale la fin — jamais déduite d'une page plus courte que demandée (même piège que
    /// <see cref="ISourceIngester.GetNewItemsAsync"/>, voir son implémentation Raindrop).
    /// </summary>
    Task<IReadOnlyList<LibraryItem>> GetLibraryPageAsync(int page, CancellationToken cancellationToken);

    /// <summary>
    /// Un raindrop existant par id (L3/<c>ReconciliationJob</c>, lot 6). <c>null</c> si l'item n'existe
    /// plus (404) — supprimé définitivement de Raindrop, pas seulement mis à la corbeille.
    /// </summary>
    Task<RaindropSnapshot?> GetRaindropAsync(long raindropId, CancellationToken cancellationToken);

    /// <summary>Crée une nouvelle collection (lot 9, #50 — provisioning d'un sujet de veille). Retourne son id.</summary>
    Task<long> CreateCollectionAsync(string title, CancellationToken cancellationToken);

    /// <summary>
    /// Crée un nouveau raindrop directement dans <paramref name="collectionId"/> (lot 9, #50) — contrairement
    /// à <see cref="UpdateRaindropAsync"/>, ce n'est pas une modification d'un item existant : c'est le seul
    /// cas du projet où une source non-Raindrop (ici, une entrée de veille via Miniflux) provoque une
    /// création dans Raindrop, jamais une modification de contenu déjà trié par l'utilisateur (ADR 0012).
    /// </summary>
    Task CreateRaindropAsync(string url, string title, long collectionId, IReadOnlyCollection<string> tags, string? note, CancellationToken cancellationToken);
}
