namespace LoreAI.Infrastructure.Persistence;

/// <summary>
/// Base d'outils (S7, lot 5) : une ligne par outil/produit distinct rencontré via un article classifié
/// <c>Action == ATester</c>. Rapprochement par <c>Name</c> insensible à la casse, fait en code (pas de
/// contrainte unique en base — un seul Worker, traitement séquentiel, pas de risque de course).
/// </summary>
public sealed class ToolEntity
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public string? Category { get; set; }

    /// <summary>
    /// Champ manuel/futur : jamais écrasé par le pipeline de classification, seulement initialisé à la
    /// création (<see cref="ToolRepository"/>). Pas d'enum : rien ne fait encore transitionner ce statut.
    /// </summary>
    public string Status { get; set; } = "À évaluer";

    public string? Verdict { get; set; }

    /// <summary>Identifiants Raindrop des articles ayant mentionné cet outil, même patron que <see cref="ArticleEntity.OriginalTags"/>.</summary>
    public long[] RelatedArticleIds { get; set; } = [];

    public DateTimeOffset FirstSeenAtUtc { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
}
