using LoreAI.Core.Enums;

namespace LoreAI.Core.Models;

/// <summary>
/// Une ligne par cycle de <c>UnsortedClassificationJob</c>, cycles vides compris — le seul signal fiable
/// que le worker tourne (healthcheck, ADR implicite issue #35) et la matière du futur compte-rendu Discord
/// (issue #31). Écrite une seule fois, en fin de cycle : <see cref="CompletedUtc"/> est donc toujours connu.
/// </summary>
public sealed record CycleRun(
    DateTimeOffset StartedUtc,
    DateTimeOffset CompletedUtc,
    CycleOutcome Outcome,
    int ItemsSeen,
    int ItemsProcessed,
    int Moved,
    int TagsApplied,
    int Notified,
    string? FailureReason);
