using LoreAI.Core.Enums;

namespace LoreAI.Core.Models;

/// <summary>
/// Article à réconcilier (L3, lot 6) : assez de contexte pour reconstituer ce que LoreAI a réellement
/// écrit dans Raindrop (tags fusionnés = <see cref="OriginalTags"/> ∪ <see cref="SuggestedTags"/>,
/// collection = <see cref="WriteBackCollectionId"/>) et décider d'une relance (L4).
/// </summary>
public sealed record ReconciliationCandidate(
    long Id,
    string Title,
    string Url,
    IReadOnlyList<string> OriginalTags,
    IReadOnlyList<string> SuggestedTags,
    long? WriteBackCollectionId,
    RecommendedAction Action,
    Priority Priority,
    DateTimeOffset? ClassifiedAtUtc,
    DateTimeOffset? HumanHandledAtUtc,
    DateTimeOffset? RemindedAtUtc);
