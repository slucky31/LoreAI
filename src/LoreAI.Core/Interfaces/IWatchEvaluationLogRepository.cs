namespace LoreAI.Core.Interfaces;

/// <summary>
/// Audit minimal des appels LLM de veille (lot 9, #50) — un appel par entrée candidate, distinct de la
/// classification par item. N'existe que pour alimenter S6 (<c>LlmUsageAnalyzer</c>) : aucun lien vers
/// <see cref="Item"/>, une entrée de veille n'étant jamais persistée (ADR 0012 : jamais de write-back pour
/// une source non-Raindrop, et ici il n'y a même pas de collection/tag concerné). Même patron que
/// <see cref="IEmailExtractionLogRepository"/> (lot 8).
/// </summary>
public interface IWatchEvaluationLogRepository
{
    Task RecordAsync(string rawResponse, DateTimeOffset processedAtUtc, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetRawResponsesSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken);
}
