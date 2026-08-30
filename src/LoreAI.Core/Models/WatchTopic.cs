namespace LoreAI.Core.Models;

/// <summary>
/// Sujet de veille (C4, lot 9, #50) — provisionné par la commande <c>--add-watch-topic</c>, pas en config
/// statique : chaque sujet a sa propre collection Raindrop et sa propre catégorie Miniflux, créées à ce
/// moment-là. <see cref="LastMinifluxEntryId"/> est le curseur d'ingestion, propre au sujet (pas à une
/// <c>SourceType</c> partagée) — <c>null</c> tant que le sujet n'a jamais été balayé.
/// </summary>
public sealed record WatchTopic(
    long Id,
    string Name,
    string Description,
    int MinifluxCategoryId,
    long RaindropCollectionId,
    string? LastMinifluxEntryId,
    DateTimeOffset CreatedAtUtc);
