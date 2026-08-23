using LoreAI.Core.Enums;
using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

/// <summary>Une implémentation par source (Raindrop, puis Newsletter/Feed — ADR 0012), chacune avec son propre curseur de polling.</summary>
public interface ISourceIngester
{
    SourceType SourceType { get; }

    /// <summary>Récupère, du plus ancien au plus récent, les items apparus après l'état de polling fourni.</summary>
    Task<IReadOnlyList<Item>> GetNewItemsAsync(PollingState lastState, CancellationToken cancellationToken);
}
