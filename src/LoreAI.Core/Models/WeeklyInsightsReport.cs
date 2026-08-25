namespace LoreAI.Core.Models;

/// <summary>
/// Agrégat des scénarios de l'axe « Opérer »/« Nettoyer »/« Synthétiser » : N1 (doublons), N2 (hygiène
/// des tags), N5 (collections déséquilibrées), S3 (tendances), S6 (coût LLM) livrés au lot 2 (#43), et
/// N3 (liens morts suivis), N4 (péremption), L1 (file de lecture scorée) ajoutés au lot 6 (#47), tous
/// alimentés par L3 (<c>ReconciliationJob</c>). Produit par <c>WeeklyInsightsJob</c>, mis en forme par
/// <c>MarkdownReportBuilder</c>.
/// </summary>
public sealed record WeeklyInsightsReport(
    IReadOnlyList<DuplicateUrlGroup> DuplicateUrls,
    TagHygieneResult TagHygiene,
    IReadOnlyList<UnbalancedCollection> UnbalancedCollections,
    IReadOnlyList<DomainTrend> TopDomains,
    IReadOnlyList<TagTrend> TopTags,
    LlmUsageSummary LlmUsage,
    IReadOnlyList<BrokenTrackedArticle> BrokenTrackedArticles,
    IReadOnlyList<StaleArticle> StaleArticles,
    IReadOnlyList<ReadingQueueEntry> ReadingQueue,
    DateTimeOffset GeneratedAtUtc);
