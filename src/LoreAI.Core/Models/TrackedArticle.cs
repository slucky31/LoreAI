using LoreAI.Core.Enums;

namespace LoreAI.Core.Models;

/// <summary>
/// Projection d'un article classifié (jamais un repli) pour les insights N3 (liens morts), N4
/// (péremption) et L1 (file de lecture scorée) — lot 6.
/// </summary>
public sealed record TrackedArticle(
    long Id,
    string Title,
    string Url,
    RecommendedAction Action,
    Priority Priority,
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset? ClassifiedAtUtc,
    int? WordCount,
    DateTimeOffset? HumanHandledAtUtc,
    LinkStatus? LinkStatus,
    SourceType SourceType,
    string SourceId);
