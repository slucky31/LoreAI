using LoreAI.Core.Enums;

namespace LoreAI.Core.Models;

/// <summary>
/// Article à réconcilier (L3, lot 6) : assez de contexte pour reconstituer ce que LoreAI a réellement
/// écrit dans Raindrop (tags fusionnés = <see cref="OriginalTags"/> ∪ <see cref="SuggestedTags"/>,
/// collection = <see cref="WriteBackCollectionId"/>) et décider d'une relance (L4).
/// </summary>
/// <param name="Id">Id technique (<c>Articles.Id</c>, généré par la base depuis le lot 8) — sert uniquement à ré-écrire la ligne via <see cref="Interfaces.IArticleRepository.RecordReconciliationAsync"/>, jamais à interroger Raindrop.</param>
/// <param name="SourceId">Id Raindrop réel (le seul valable pour <see cref="Interfaces.IRaindropClient.GetRaindropAsync"/>) — cette source est toujours <c>SourceType.Raindrop</c>, filtré en amont par <see cref="Interfaces.IArticleRepository.GetReconciliationCandidatesAsync"/>.</param>
public sealed record ReconciliationCandidate(
    long Id,
    string SourceId,
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
