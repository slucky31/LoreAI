namespace LoreAI.Core.Models;

/// <summary>
/// Agrégat des cinq scénarios de l'axe « Opérer »/« Nettoyer »/« Synthétiser » livrés au lot 2 (#43) :
/// N1 (doublons), N2 (hygiène des tags), N5 (collections déséquilibrées), S3 (tendances), S6 (coût LLM).
/// Produit par <c>WeeklyInsightsJob</c>, mis en forme par <c>MarkdownReportBuilder</c>.
/// </summary>
public sealed record WeeklyInsightsReport(
    IReadOnlyList<DuplicateUrlGroup> DuplicateUrls,
    TagHygieneResult TagHygiene,
    IReadOnlyList<UnbalancedCollection> UnbalancedCollections,
    IReadOnlyList<DomainTrend> TopDomains,
    IReadOnlyList<TagTrend> TopTags,
    LlmUsageSummary LlmUsage,
    DateTimeOffset GeneratedAtUtc);
