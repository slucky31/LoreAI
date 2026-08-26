namespace LoreAI.Core.Interfaces;

/// <summary>
/// Audit minimal des appels LLM d'extraction de liens (lot 8, #49) — un appel par mail traité, distinct de
/// la classification par item. N'existe que pour alimenter S6 (<c>LlmUsageAnalyzer</c>) : aucun lien vers
/// <see cref="Item"/>, la granularité mail n'a pas d'autre usage (ADR 0012 : « unité = le lien, jamais le mail »).
/// </summary>
public interface IEmailExtractionLogRepository
{
    Task RecordAsync(string rawResponse, DateTimeOffset processedAtUtc, CancellationToken cancellationToken);

    /// <summary>Réponses Anthropic brutes des extractions réalisées depuis <paramref name="sinceUtc"/> — à combiner avec <see cref="IArticleRepository.GetClassificationRawResponsesSinceAsync"/> avant <c>LlmUsageAnalyzer.Analyze</c>.</summary>
    Task<IReadOnlyList<string>> GetRawResponsesSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken);
}
