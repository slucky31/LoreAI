using LoreAI.Core.Enums;

namespace LoreAI.Core.Models;

/// <summary>
/// Projection d'un article classifié pour la revue mensuelle (S4, lot 5) : uniquement des articles
/// réellement classifiés (<c>IsFallback == false</c>), jamais un résultat de repli.
/// </summary>
public sealed record MonthlyReviewArticle(
    long Id,
    string Title,
    string Url,
    string? SuggestedCollection,
    IReadOnlyList<string> Tags,
    string? Summary,
    string? Reason,
    Priority Priority);
