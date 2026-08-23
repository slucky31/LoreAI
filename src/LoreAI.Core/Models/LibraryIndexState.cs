using LoreAI.Core.Enums;

namespace LoreAI.Core.Models;

/// <summary>
/// Curseur de <c>LibraryIndexingJob</c> (lot 1, #42) — distinct de <see cref="PollingState"/> : ce n'est pas
/// un dernier-item-connu mais une page de reprise dans un balayage complet de la bibliothèque, plus la date
/// de fin de la dernière passe complète (sert de garde anti-doublon au démarrage).
/// </summary>
public sealed record LibraryIndexState(
    SourceType SourceType,
    int? ResumePage,
    DateTimeOffset? LastFullPassStartedUtc,
    DateTimeOffset? LastFullPassCompletedUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static LibraryIndexState Initial(SourceType sourceType) => new(sourceType, null, null, null, DateTimeOffset.UnixEpoch);
}
