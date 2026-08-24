using NpgsqlTypes;

namespace LoreAI.Infrastructure.Persistence;

/// <summary>
/// Forme persistée d'un item du balayage en lecture seule de toute la bibliothèque (lot 1, #42) — table
/// distincte d'<see cref="ArticleEntity"/> (jamais classifié, pas de write-back). Même convention de clé :
/// <c>Id</c> reste l'id Raindrop numérique, pas de composite <c>(SourceType, SourceId)</c> tant qu'une
/// deuxième source n'existe pas réellement (même raisonnement qu'<see cref="ArticleEntity"/>, ADR 0012).
/// </summary>
public sealed class LibraryItemEntity
{
    public long Id { get; set; }
    public required string SourceType { get; set; }
    public required string Title { get; set; }
    public required string Url { get; set; }
    public string? Excerpt { get; set; }
    public string? Note { get; set; }
    public string[] Tags { get; set; } = [];
    public DateTimeOffset CapturedAtUtc { get; set; }

    public required string Origin { get; set; }
    public long? RaindropCollectionId { get; set; }
    public bool Broken { get; set; }
    public bool Important { get; set; }
    public string? Cover { get; set; }
    public string? HighlightsJson { get; set; }

    public DateTimeOffset IndexedAtUtc { get; set; }

    /// <summary>
    /// Colonne générée par Postgres (dictionnaire <c>french</c>, titre + extrait) — Q2, lot 5. Jamais
    /// affectée en code : <c>HasGeneratedTsVectorColumn</c> dans <see cref="LoreAiDbContext"/> délègue
    /// entièrement le calcul à la base.
    /// </summary>
    public NpgsqlTsVector SearchVector { get; set; } = null!;
}
