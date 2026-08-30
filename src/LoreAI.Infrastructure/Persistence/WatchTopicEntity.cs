namespace LoreAI.Infrastructure.Persistence;

/// <summary>Forme persistée d'un sujet de veille (lot 9, #50, redesign) — créé par le mode CLI <c>--add-watch-topic</c>, lu par <c>TopicWatchJob</c>.</summary>
public sealed class WatchTopicEntity
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public int MinifluxCategoryId { get; set; }
    public long RaindropCollectionId { get; set; }

    /// <summary>Curseur d'ingestion, propre à ce sujet — <c>null</c> tant qu'il n'a jamais été balayé (jamais le cas en pratique : seedé à "0" à la création, catégorie vide).</summary>
    public string? LastMinifluxEntryId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
