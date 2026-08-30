namespace LoreAI.Infrastructure.Persistence;

/// <summary>
/// Forme persistée d'un appel LLM d'évaluation de veille (lot 9, #50) — même patron que
/// <see cref="EmailExtractionLogEntity"/> : pas de clé applicative, <see cref="Id"/> est généré par la base.
/// N'existe que pour S6 (<c>LlmUsageAnalyzer</c>).
/// </summary>
public sealed class WatchEvaluationLogEntity
{
    public long Id { get; set; }
    public DateTimeOffset ProcessedAtUtc { get; set; }
    public required string RawResponse { get; set; }
}
