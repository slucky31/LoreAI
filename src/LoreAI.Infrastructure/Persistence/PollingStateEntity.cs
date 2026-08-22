namespace LoreAI.Infrastructure.Persistence;

/// <summary>Ligne unique (Id = 1) portant le curseur de polling. Voir <see cref="ArticleEntity"/> pour la convention.</summary>
public sealed class PollingStateEntity
{
    public int Id { get; set; }
    public long? LastRaindropId { get; set; }
    public DateTimeOffset? LastCreatedUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
