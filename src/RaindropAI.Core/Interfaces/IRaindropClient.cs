using RaindropAI.Core.Models;

namespace RaindropAI.Core.Interfaces;

public interface IRaindropClient
{
    /// <summary>
    /// Récupère, du plus ancien au plus récent, les raindrops créés après l'état de polling fourni.
    /// Pagine tant que nécessaire et s'arrête dès qu'un raindrop déjà connu est rencontré.
    /// </summary>
    Task<IReadOnlyList<RaindropItem>> GetNewRaindropsAsync(PollingState lastState, CancellationToken cancellationToken);

    /// <summary>Apprend les collections et tags réellement en place dans le compte de l'utilisateur.</summary>
    Task<RaindropTaxonomy> GetTaxonomyAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Met à jour un raindrop existant : tags et note (fusionnés côté appelant, jamais écrasés ici),
    /// et déplacement optionnel vers une autre collection si <paramref name="collectionId"/> est fourni.
    /// </summary>
    Task UpdateRaindropAsync(long raindropId, IReadOnlyCollection<string> tags, string note, long? collectionId, CancellationToken cancellationToken);
}
