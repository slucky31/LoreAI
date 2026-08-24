using LoreAI.Core.Models;

namespace LoreAI.Core.Services;

/// <summary>Regroupement pur des articles du mois par thème (S4, lot 5) — testable sans LLM ni base.</summary>
public static class MonthlyReviewGrouper
{
    public const string UnclassifiedTheme = "Non classé";

    /// <summary>Regroupe par <c>SuggestedCollection</c> (ou <see cref="UnclassifiedTheme"/> si absente), du groupe le plus fourni au moins fourni, puis par nom de thème.</summary>
    public static IReadOnlyList<(string Theme, IReadOnlyList<MonthlyReviewArticle> Articles)> GroupByTheme(IReadOnlyList<MonthlyReviewArticle> articles)
    {
        return articles
            .GroupBy(a => a.SuggestedCollection ?? UnclassifiedTheme)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (Theme: g.Key, Articles: (IReadOnlyList<MonthlyReviewArticle>)g.ToList()))
            .ToList();
    }
}
