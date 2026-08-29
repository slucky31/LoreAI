using LoreAI.Core.Enums;

namespace LoreAI.Core.Models;

/// <summary>Entrée de la file de lecture scorée (L1, lot 6) — priorité × fraîcheur × temps de lecture.</summary>
public sealed record ReadingQueueEntry(
    long Id,
    string Title,
    string Url,
    double Score,
    int? EstimatedMinutes,
    Priority Priority,
    DateTimeOffset CapturedAtUtc,
    SourceType SourceType,
    string SourceId);
