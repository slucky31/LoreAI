namespace LoreAI.Core.Models;

/// <summary>Article ayant mentionné un outil (S7, lot 5) — assez de détail pour une fiche Markdown.</summary>
public sealed record ToolRelatedArticle(long Id, string Title, string Url, string? Summary);

/// <summary>Détail complet d'un outil (S7, lot 5), utilisé pour projeter la fiche Markdown régénérée.</summary>
public sealed record ToolCard(
    long Id,
    string Name,
    string? Category,
    string Status,
    string? Verdict,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset LastSeenAtUtc,
    IReadOnlyList<ToolRelatedArticle> RelatedArticles,
    string? Url = null);
