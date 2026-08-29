using Microsoft.EntityFrameworkCore;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;
using LoreAI.Core.Services;

namespace LoreAI.Infrastructure.Persistence;

/// <summary>
/// N'appelle jamais <see cref="PostgresSchemaGuard"/> (voir <see cref="ICorpusQueryRepository"/>) : conçu
/// pour tourner avec le rôle <c>loreai_ro</c> (ADR 0009, ADR 0014), qui n'a pas les privilèges de
/// migration. Le schéma est garanti à jour par le Worker, seul propriétaire de la base.
/// </summary>
public sealed class CorpusQueryRepository : ICorpusQueryRepository
{
    private readonly IDbContextFactory<LoreAiDbContext> _contextFactory;

    public CorpusQueryRepository(IDbContextFactory<LoreAiDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<LibraryItemSummary?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.LibraryItems
            .Where(i => i.Id == id)
            .Select(ToSummary)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LibraryItemSummary>> GetRecentAsync(int count, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.LibraryItems
            .OrderByDescending(i => i.CapturedAtUtc)
            .Take(count)
            .Select(ToSummary)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Recherche plein texte (Q2, lot 5) : <c>websearch_to_tsquery</c> contre la colonne <c>SearchVector</c>
    /// générée, classée par pertinence (<c>ts_rank_cd</c>). <c>EF.Functions.WebSearchToTsQuery</c> doit
    /// rester inline dans chaque lambda LINQ (pas extrait en variable) : c'est un marqueur de traduction
    /// SQL sans corps réel, appelé en dehors d'une expression tree il lève au runtime.
    /// </summary>
    public async Task<IReadOnlyList<LibraryItemSummary>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.LibraryItems
            .Where(i => i.SearchVector.Matches(EF.Functions.WebSearchToTsQuery("french", query)))
            .OrderByDescending(i => i.SearchVector.RankCoverDensity(EF.Functions.WebSearchToTsQuery("french", query)))
            .Take(limit)
            .Select(ToSummary)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// S5 (lot 5) : dérive une requête plein texte en <c>OR</c> à partir des mots du titre de l'item source,
    /// puis classe les autres items par pertinence contre cette requête. Recherche plein texte plutôt que
    /// des embeddings — cf. l'arbitrage du roadmap sur S5 (« recherche plein texte d'abord »).
    /// </summary>
    public async Task<IReadOnlyList<LibraryItemSummary>> FindSimilarAsync(long id, int limit, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var source = await context.LibraryItems.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (source is null)
        {
            return [];
        }

        var words = source.Title
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (words.Count == 0)
        {
            return [];
        }

        var queryText = string.Join(" or ", words);
        return await context.LibraryItems
            .Where(i => i.Id != id && i.SearchVector.Matches(EF.Functions.WebSearchToTsQuery("french", queryText)))
            .OrderByDescending(i => i.SearchVector.RankCoverDensity(EF.Functions.WebSearchToTsQuery("french", queryText)))
            .Take(limit)
            .Select(ToSummary)
            .ToListAsync(cancellationToken);
    }

    public async Task<CorpusStats> GetStatsAsync(CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var totalItems = await context.LibraryItems.CountAsync(cancellationToken);
        var importantItems = await context.LibraryItems.CountAsync(i => i.Important, cancellationToken);
        var brokenItems = await context.LibraryItems.CountAsync(i => i.Broken, cancellationToken);
        var lastIndexedAtUtc = await context.LibraryItems.MaxAsync(i => (DateTimeOffset?)i.IndexedAtUtc, cancellationToken);

        return new CorpusStats(totalItems, importantItems, brokenItems, lastIndexedAtUtc);
    }

    public async Task<IReadOnlyList<ToolSummary>> GetToolsAsync(CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Tools
            .OrderByDescending(t => t.LastSeenAtUtc)
            .Select(t => new ToolSummary(t.Id, t.Name, t.Category, t.Status, t.Verdict, t.RelatedArticleIds.Length, t.FirstSeenAtUtc, t.LastSeenAtUtc))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Jointure sur <c>Articles</c> via <c>RelatedArticleIds</c> (EF Core/Npgsql traduit <c>Contains</c> sur un tableau en <c>= ANY(...)</c>).</summary>
    public async Task<ToolCard?> GetToolByNameAsync(string name, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var tool = await context.Tools.SingleOrDefaultAsync(t => EF.Functions.ILike(t.Name, name), cancellationToken);
        if (tool is null)
        {
            return null;
        }

        var relatedArticles = await context.Articles
            .Where(a => tool.RelatedArticleIds.Contains(a.Id))
            .Select(a => new ToolRelatedArticle(a.Id, a.Title, a.Url, a.Summary))
            .ToListAsync(cancellationToken);

        return new ToolCard(tool.Id, tool.Name, tool.Category, tool.Status, tool.Verdict, tool.FirstSeenAtUtc, tool.LastSeenAtUtc, relatedArticles, tool.Url);
    }

    public async Task<string?> GetArticleSummaryAsync(long id, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Articles
            .Where(a => a.Id == id)
            .Select(a => a.Summary)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>L1 (lot 6) : projection puis scoring en mémoire (<see cref="ReadingQueueScorer"/>) — le corpus suivi reste petit face à toute la bibliothèque, pas besoin de traduire le score en SQL.</summary>
    public async Task<IReadOnlyList<ReadingQueueEntry>> GetReadingQueueAsync(int limit, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var trackedArticles = await context.Articles
            .Where(a => !a.IsFallback)
            .Select(a => new TrackedArticle(
                a.Id, a.Title, a.Url, a.RecommendedAction, a.Priority, a.CapturedAtUtc,
                a.ClassifiedAtUtc, a.WordCount, a.HumanHandledAtUtc, a.LinkStatus, a.SourceType, a.SourceId))
            .ToListAsync(cancellationToken);

        return ReadingQueueScorer.Score(trackedArticles, DateTimeOffset.UtcNow, limit);
    }

    private static readonly System.Linq.Expressions.Expression<Func<LibraryItemEntity, LibraryItemSummary>> ToSummary =
        i => new LibraryItemSummary(i.Id, i.Title, i.Url, i.Tags, i.RaindropCollectionId, i.CapturedAtUtc);
}
