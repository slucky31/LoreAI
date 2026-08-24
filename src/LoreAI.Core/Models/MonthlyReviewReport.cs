namespace LoreAI.Core.Models;

/// <summary>Narration générée pour un thème (S4, lot 5), avec les articles qui l'ont alimentée.</summary>
public sealed record ThemeReview(string Theme, string Narrative, IReadOnlyList<MonthlyReviewArticle> Articles);

/// <summary>Revue mensuelle complète (S4, lot 5), envoyée en pièce jointe Discord.</summary>
public sealed record MonthlyReviewReport(
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc,
    IReadOnlyList<ThemeReview> Themes,
    DateTimeOffset GeneratedAtUtc);
