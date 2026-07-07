using RaindropAI.Core.Models;

namespace RaindropAI.Core.Interfaces;

public interface IRaindropClient
{
    /// <summary>
    /// Récupère, du plus ancien au plus récent, les raindrops créés après l'état de polling fourni.
    /// Pagine tant que nécessaire et s'arrête dès qu'un raindrop déjà connu est rencontré.
    /// </summary>
    Task<IReadOnlyList<RaindropItem>> GetNewRaindropsAsync(PollingState lastState, CancellationToken cancellationToken);

    /// <summary>
    /// Met à jour un raindrop existant (tags/note) — utilisé uniquement si l'écriture en retour est activée.
    /// </summary>
    Task UpdateRaindropAsync(long raindropId, IReadOnlyCollection<string> tags, string note, CancellationToken cancellationToken);
}
