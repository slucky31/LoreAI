namespace LoreAI.Core.Models;

/// <summary>
/// Consommation LLM cumulée sur la période du rapport (S6) — le garde-fou du budget mensuel avant toute
/// dépense supplémentaire (lots 4/5/9, voir roadmap). <see cref="EstimatedCostUsd"/> ne couvre que
/// <see cref="InputTokens"/>/<see cref="OutputTokens"/> aux tarifs Claude Haiku 4.5 (1 $ / 5 $ le million
/// de tokens) ; les tokens de cache sont exposés séparément (issue #31) sans entrer dans l'estimation,
/// leur tarif différant de celui des tokens standard.
/// </summary>
public sealed record LlmUsageSummary(
    int ClassificationCount,
    long InputTokens,
    long OutputTokens,
    long CacheCreationInputTokens,
    long CacheReadInputTokens,
    decimal EstimatedCostUsd);
