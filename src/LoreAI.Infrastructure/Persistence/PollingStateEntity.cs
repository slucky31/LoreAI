namespace LoreAI.Infrastructure.Persistence;

/// <summary>Une ligne par source (clé <see cref="SourceType"/>) — ADR 0012. Voir <see cref="ArticleEntity"/> pour la convention.</summary>
public sealed class PollingStateEntity
{
    public required string SourceType { get; set; }
    public string? LastSourceItemId { get; set; }
    public DateTimeOffset? LastCreatedUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
