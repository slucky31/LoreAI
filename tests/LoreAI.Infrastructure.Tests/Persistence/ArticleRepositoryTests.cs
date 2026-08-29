using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using LoreAI.Core.Enums;
using LoreAI.Core.Models;
using LoreAI.Infrastructure.Persistence;

namespace LoreAI.Infrastructure.Tests.Persistence;

[Collection("Postgres")]
public class ArticleRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly ArticleRepository _repository;

    public ArticleRepositoryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _repository = new ArticleRepository(fixture.ContextFactory, new PostgresSchemaGuard(fixture.ContextFactory), NullLogger<ArticleRepository>.Instance);
    }

    // Une nouvelle instance de la classe est créée par xUnit pour chaque test : tronquer ici équivaut à
    // l'ancien fichier SQLite frais par test, sans payer un conteneur par test.
    public async ValueTask InitializeAsync()
    {
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Articles\" RESTART IDENTITY CASCADE");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task UpsertAsync_CalledTwiceWithSameId_DoesNotDuplicate()
    {
        var item = CreateItem(1, "Titre initial");
        var classification = CreateClassification();

        await _repository.UpsertAsync(item, classification, ContentFetchResult.Skipped, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        await _repository.UpsertAsync(item with { Title = "Titre mis à jour" }, classification, ContentFetchResult.Skipped, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        var all = await GetAllAsync();

        var single = Assert.Single(all);
        Assert.Equal("Titre mis à jour", single.Title);
    }

    [Fact]
    public async Task MarkDiscordNotifiedAsync_SetsTimestamp()
    {
        await _repository.UpsertAsync(CreateItem(1, "A"), CreateClassification(), ContentFetchResult.Skipped, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        await _repository.MarkDiscordNotifiedAsync(1, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        var entity = await GetAsync(1);
        Assert.NotNull(entity.DiscordNotifiedAtUtc);
    }

    [Fact]
    public async Task RecordWriteBackAsync_Moved_SetsMovedFlag()
    {
        await _repository.UpsertAsync(CreateItem(1, "A"), CreateClassification(), ContentFetchResult.Skipped, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        await _repository.RecordWriteBackAsync(1, success: true, moved: true, writeBackCollectionId: 42, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        var entity = await GetAsync(1);
        Assert.True(entity.Moved);
    }

    [Fact]
    public async Task RecordWriteBackAsync_TagsOnly_LeavesMovedFalse()
    {
        await _repository.UpsertAsync(CreateItem(1, "A"), CreateClassification(), ContentFetchResult.Skipped, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        await _repository.RecordWriteBackAsync(1, success: true, moved: false, writeBackCollectionId: null, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        var entity = await GetAsync(1);
        Assert.False(entity.Moved);
    }

    [Fact]
    public async Task UpsertAsync_RoundTripsTagsAndSuggestedCollection()
    {
        var item = CreateItem(1, "A") with { Tags = ["dotnet", "claude"] };
        var classification = new ClassificationResult("Claude", ["claude", "ia"], RecommendedAction.ATester, Priority.Haute, "raison", "résumé", "claude-haiku-4-5", "{}");

        await _repository.UpsertAsync(item, classification, ContentFetchResult.Skipped, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        var entity = await GetAsync(1);

        Assert.Equal(["dotnet", "claude"], entity.OriginalTags);
        Assert.Equal("Claude", entity.SuggestedCollection);
        Assert.Equal(["claude", "ia"], entity.SuggestedTags);
        Assert.Equal(RecommendedAction.ATester, entity.RecommendedAction);
        Assert.Equal(Priority.Haute, entity.Priority);
        Assert.Equal("résumé", entity.Summary);
    }

    [Fact]
    public async Task UpsertAsync_RoundTripsContentFetchResult()
    {
        var content = new ContentFetchResult(ContentFetchStatus.Success, "texte complet de l'article", 42);

        await _repository.UpsertAsync(CreateItem(1, "A"), CreateClassification(), content, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        var entity = await GetAsync(1);

        Assert.Equal("texte complet de l'article", entity.ContentText);
        Assert.Equal(ContentFetchStatus.Success, entity.ContentStatus);
        Assert.Equal(42, entity.WordCount);
        Assert.NotNull(entity.ContentFetchedAtUtc);
    }

    [Fact]
    public async Task UpsertAsync_SkippedContent_LeavesContentFetchedAtUtcNull()
    {
        await _repository.UpsertAsync(CreateItem(1, "A"), CreateClassification(), ContentFetchResult.Skipped, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        var entity = await GetAsync(1);

        Assert.Null(entity.ContentText);
        Assert.Equal(ContentFetchStatus.Skipped, entity.ContentStatus);
        Assert.Null(entity.ContentFetchedAtUtc);
    }

    [Fact]
    public async Task UpsertAsync_NullSuggestedCollection_RoundTripsAsNull()
    {
        var item = CreateItem(1, "A");
        var classification = CreateClassification();

        await _repository.UpsertAsync(item, classification, ContentFetchResult.Skipped, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        var entity = await GetAsync(1);

        Assert.Null(entity.SuggestedCollection);
    }

    /// <summary>
    /// Régression : une panne de transport avant toute réponse HTTP laisse `RawResponse` vide
    /// (`AnthropicClassifier`), et `ClassificationRawResponse` est désormais un vrai jsonb — sans
    /// normalisation, l'insertion échouerait avec « invalid input syntax for type json » et l'article de
    /// repli, censé ne jamais se perdre, disparaîtrait silencieusement dans une exception non gérée.
    /// </summary>
    [Fact]
    public async Task UpsertAsync_EmptyRawResponse_DoesNotThrow()
    {
        var classification = CreateClassification() with { RawResponse = string.Empty };

        await _repository.UpsertAsync(CreateItem(1, "A"), classification, ContentFetchResult.Skipped, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        Assert.Single(await GetAllAsync());
    }

    /// <summary>
    /// Lot 8 (#49) : Id est désormais généré par la base, la clé applicative devient (SourceType, SourceId).
    /// Un lien Newsletter portant le même SourceId textuel qu'un article Raindrop (ex. "1") ne doit ni
    /// entrer en collision, ni se faire écraser par l'autre.
    /// </summary>
    [Fact]
    public async Task UpsertAsync_SameSourceIdDifferentSourceType_DoesNotCollide()
    {
        var raindropItem = CreateItem(1, "Article Raindrop");
        var newsletterItem = raindropItem with { SourceType = SourceType.Newsletter, Title = "Lien newsletter" };

        var raindropId = await _repository.UpsertAsync(raindropItem, CreateClassification(), ContentFetchResult.Skipped, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        var newsletterId = await _repository.UpsertAsync(newsletterItem, CreateClassification(), ContentFetchResult.Skipped, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        Assert.NotEqual(raindropId, newsletterId);
        var all = await GetAllAsync();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, a => a.SourceType == SourceType.Raindrop && a.Title == "Article Raindrop");
        Assert.Contains(all, a => a.SourceType == SourceType.Newsletter && a.Title == "Lien newsletter");
    }

    /// <summary>
    /// Régression : <c>GetReconciliationCandidatesAsync</c> ne doit renvoyer que des articles Raindrop
    /// (seule source qu'<c>IRaindropClient.GetRaindropAsync</c> sait interroger), et exposer le vrai id
    /// Raindrop via <c>SourceId</c> — jamais <c>Id</c>, l'id technique généré par la base depuis le lot 8.
    /// </summary>
    [Fact]
    public async Task GetReconciliationCandidatesAsync_ExcludesNonRaindropSourcesAndExposesSourceId()
    {
        var raindropItem = CreateItem(42, "Article Raindrop");
        var newsletterItem = raindropItem with { SourceType = SourceType.Newsletter, SourceId = "42", Title = "Lien newsletter" };
        await _repository.UpsertAsync(raindropItem, CreateClassification(), ContentFetchResult.Skipped, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        await _repository.UpsertAsync(newsletterItem, CreateClassification(), ContentFetchResult.Skipped, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        var candidates = await _repository.GetReconciliationCandidatesAsync(10, TestContext.Current.CancellationToken);

        var single = Assert.Single(candidates);
        Assert.Equal("Article Raindrop", single.Title);
        Assert.Equal("42", single.SourceId);
        Assert.NotEqual(single.Id, long.Parse(single.SourceId, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task SetReadingQueueTagAsync_ThenGetReadingQueueTaggedAsync_RoundTrips()
    {
        var articleId = await _repository.UpsertAsync(CreateItem(1, "A"), CreateClassification(), ContentFetchResult.Skipped, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        await _repository.SetReadingQueueTagAsync(articleId, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        var tagged = await _repository.GetReadingQueueTaggedAsync(TestContext.Current.CancellationToken);

        var single = Assert.Single(tagged);
        Assert.Equal(articleId, single.ArticleId);
        Assert.Equal("1", single.SourceId);
    }

    [Fact]
    public async Task SetReadingQueueTagAsync_Null_ClearsTracking()
    {
        var articleId = await _repository.UpsertAsync(CreateItem(1, "A"), CreateClassification(), ContentFetchResult.Skipped, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        await _repository.SetReadingQueueTagAsync(articleId, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        await _repository.SetReadingQueueTagAsync(articleId, null, TestContext.Current.CancellationToken);

        Assert.Empty(await _repository.GetReadingQueueTaggedAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetReadingQueueTaggedAsync_ExcludesNonRaindropSources()
    {
        var raindropItem = CreateItem(1, "Article Raindrop");
        var newsletterItem = raindropItem with { SourceType = SourceType.Newsletter, SourceId = "1", Title = "Lien newsletter" };
        var raindropId = await _repository.UpsertAsync(raindropItem, CreateClassification(), ContentFetchResult.Skipped, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        var newsletterId = await _repository.UpsertAsync(newsletterItem, CreateClassification(), ContentFetchResult.Skipped, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        await _repository.SetReadingQueueTagAsync(raindropId, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        await _repository.SetReadingQueueTagAsync(newsletterId, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        var tagged = await _repository.GetReadingQueueTaggedAsync(TestContext.Current.CancellationToken);

        var single = Assert.Single(tagged);
        Assert.Equal(raindropId, single.ArticleId);
    }

    [Fact]
    public async Task GetClassificationRawResponsesSinceAsync_ExcludesArticlesClassifiedBeforeCutoff()
    {
        var cutoff = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        await _repository.UpsertAsync(
            CreateItem(1, "Avant"), CreateClassification() with { RawResponse = "{\"usage\":{}}" }, ContentFetchResult.Skipped, cutoff.AddDays(-1), TestContext.Current.CancellationToken);
        await _repository.UpsertAsync(
            CreateItem(2, "Après"), CreateClassification() with { RawResponse = "{\"usage\":{\"input_tokens\":10}}" }, ContentFetchResult.Skipped, cutoff.AddDays(1), TestContext.Current.CancellationToken);

        var responses = await _repository.GetClassificationRawResponsesSinceAsync(cutoff, TestContext.Current.CancellationToken);

        var single = Assert.Single(responses);
        Assert.Contains("10", single);
    }

    private async Task<ArticleEntity> GetAsync(long id)
    {
        await using var context = _fixture.CreateContext();
        return await context.Articles.SingleAsync(a => a.Id == id, TestContext.Current.CancellationToken);
    }

    private async Task<List<ArticleEntity>> GetAllAsync()
    {
        await using var context = _fixture.CreateContext();
        return await context.Articles.ToListAsync(TestContext.Current.CancellationToken);
    }

    private static Item CreateItem(long id, string title) => new(
        SourceType.Raindrop,
        id.ToString(CultureInfo.InvariantCulture),
        "https://example.com",
        title,
        "extrait",
        "note",
        [],
        DateTimeOffset.UtcNow);

    private static ClassificationResult CreateClassification() =>
        // "raw" n'est plus une valeur de test valide pour ClassificationRawResponse : la colonne est
        // désormais un vrai jsonb, qui rejette tout ce qui n'est pas du JSON valide.
        new(null, [], RecommendedAction.ALire, Priority.Moyenne, "raison", "résumé", "model", "{}");
}
