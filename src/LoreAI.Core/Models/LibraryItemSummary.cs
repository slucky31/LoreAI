namespace LoreAI.Core.Models;

/// <summary>
/// Projection allégée d'un <see cref="LibraryItem"/> pour les besoins des insights hebdomadaires
/// (<c>WeeklyInsightsJob</c>, #43) : pas de <c>HighlightsJson</c>/<c>Cover</c>/<c>Excerpt</c>/<c>Note</c>,
/// jamais nécessaires pour détecter doublons, tags ou déséquilibres de collections, et coûteux à charger
/// en mémoire sur toute la bibliothèque.
/// </summary>
public sealed record LibraryItemSummary(
    long Id,
    string Title,
    string Url,
    IReadOnlyList<string> Tags,
    long? RaindropCollectionId,
    DateTimeOffset CapturedAtUtc);
