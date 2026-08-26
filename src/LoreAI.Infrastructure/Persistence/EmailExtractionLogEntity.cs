namespace LoreAI.Infrastructure.Persistence;

/// <summary>
/// Forme persistée d'un appel LLM d'extraction de liens (lot 8, #49) — même patron que
/// <see cref="CycleRunEntity"/> : pas de clé applicative, <see cref="Id"/> est généré par la base.
/// N'existe que pour S6 (<c>LlmUsageAnalyzer</c>).
/// </summary>
public sealed class EmailExtractionLogEntity
{
    public long Id { get; set; }
    public DateTimeOffset ProcessedAtUtc { get; set; }
    public required string RawResponse { get; set; }
}
