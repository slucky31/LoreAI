using LoreAI.Core.Enums;

namespace LoreAI.Core.Models;

/// <summary>
/// Un item tel que vu par le balayage en lecture seule de toute la bibliothèque Raindrop (lot 1, #42) —
/// enrichit <see cref="Item"/> des champs propres à ce balayage, jamais utilisés par le pipeline de
/// classification (<c>UnsortedClassificationJob</c> continue de ne consommer que <see cref="Item"/>).
/// </summary>
public sealed record LibraryItem(
    Item Item,
    ItemOrigin Origin,
    long? RaindropCollectionId,
    bool Broken,
    bool Important,
    string? Cover,
    string? HighlightsJson);
