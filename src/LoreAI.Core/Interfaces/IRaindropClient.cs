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
}
