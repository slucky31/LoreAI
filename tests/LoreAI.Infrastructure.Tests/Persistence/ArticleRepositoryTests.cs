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

        await _repository.UpsertAsync(item, classification, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        await _repository.UpsertAsync(item with { Title = "Titre mis à jour" }, classification, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        var pending = await _repository.GetUnsentDigestItemsAsync(TestContext.Current.CancellationToken);

        var single = Assert.Single(pending);
        Assert.Equal("Titre mis à jour", single.Item.Title);
    }

    [Fact]
    public async Task GetUnsentDigestItemsAsync_ExcludesArticlesAlreadySent()
    {
        await _repository.UpsertAsync(CreateItem(1, "A"), CreateClassification(), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        await _repository.UpsertAsync(CreateItem(2, "B"), CreateClassification(), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        await _repository.MarkDigestSentAsync([1], DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        var pending = await _repository.GetUnsentDigestItemsAsync(TestContext.Current.CancellationToken);

        var single = Assert.Single(pending);
        Assert.Equal(2, single.Item.Id);
    }

    /// <summary>
    /// F-21, hérité de l'ère SQLite : la clause IN était développée en un paramètre par identifiant par
    /// Dapper, et dépassait la limite de variables de SQLite sans découpage en lots. `ExecuteUpdateAsync`
    /// (EF Core) traduit le IN en une seule requête SQL, sans cette limite côté Postgres — ce test vérifie
    /// que ça scale toujours sur un digest volumineux, pas qu'un découpage manuel fonctionne.
    /// </summary>
    [Fact]
    public async Task MarkDigestSentAsync_WithManyIds_MarksThemAll()
    {
        const int count = 1200;
        for (var id = 1; id <= count; id++)
        {
            await _repository.UpsertAsync(CreateItem(id, $"A{id}"), CreateClassification(), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        }

        await _repository.MarkDigestSentAsync(
            Enumerable.Range(1, count).Select(i => (long)i).ToList(),
            DateTimeOffset.UtcNow,
            TestContext.Current.CancellationToken);

        Assert.Empty(await _repository.GetUnsentDigestItemsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MarkDiscordNotifiedAsync_SetsTimestamp()
    {
        await _repository.UpsertAsync(CreateItem(1, "A"), CreateClassification(), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        await _repository.MarkDiscordNotifiedAsync(1, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        var pending = await _repository.GetUnsentDigestItemsAsync(TestContext.Current.CancellationToken);
        var single = Assert.Single(pending);
        Assert.NotNull(single.DiscordNotifiedAtUtc);
    }

    [Fact]
    public async Task RecordWriteBackAsync_Moved_SetsMovedFlag()
    {
        await _repository.UpsertAsync(CreateItem(1, "A"), CreateClassification(), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        await _repository.RecordWriteBackAsync(1, success: true, moved: true, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        var pending = await _repository.GetUnsentDigestItemsAsync(TestContext.Current.CancellationToken);
        var single = Assert.Single(pending);
        Assert.True(single.Moved);
    }

    [Fact]
    public async Task RecordWriteBackAsync_TagsOnly_LeavesMovedFalse()
    {
        await _repository.UpsertAsync(CreateItem(1, "A"), CreateClassification(), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        await _repository.RecordWriteBackAsync(1, success: true, moved: false, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        var pending = await _repository.GetUnsentDigestItemsAsync(TestContext.Current.CancellationToken);
        var single = Assert.Single(pending);
        Assert.False(single.Moved);
    }

    [Fact]
    public async Task UpsertAsync_RoundTripsTagsAndSuggestedCollection()
    {
        var item = CreateItem(1, "A") with { Tags = ["dotnet", "claude"] };
        var classification = new ClassificationResult("Claude", ["claude", "ia"], RecommendedAction.ATester, Priority.Haute, "raison", "claude-haiku-4-5", "{}");

        await _repository.UpsertAsync(item, classification, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        var pending = await _repository.GetUnsentDigestItemsAsync(TestContext.Current.CancellationToken);
        var single = Assert.Single(pending);

        Assert.Equal(["dotnet", "claude"], single.Item.Tags);
        Assert.Equal("Claude", single.Classification.SuggestedCollection);
        Assert.Equal(["claude", "ia"], single.Classification.Tags);
        Assert.Equal(RecommendedAction.ATester, single.Classification.Action);
        Assert.Equal(Priority.Haute, single.Classification.Priority);
    }

    [Fact]
    public async Task UpsertAsync_NullSuggestedCollection_RoundTripsAsNull()
    {
        var item = CreateItem(1, "A");
        var classification = CreateClassification();

        await _repository.UpsertAsync(item, classification, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        var pending = await _repository.GetUnsentDigestItemsAsync(TestContext.Current.CancellationToken);
        var single = Assert.Single(pending);

        Assert.Null(single.Classification.SuggestedCollection);
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

        await _repository.UpsertAsync(CreateItem(1, "A"), classification, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        var pending = await _repository.GetUnsentDigestItemsAsync(TestContext.Current.CancellationToken);
        Assert.Single(pending);
    }

    private static RaindropItem CreateItem(long id, string title) => new(
        id,
        title,
        "https://example.com",
        "extrait",
        "note",
        [],
        null,
        "example.com",
        "article",
        DateTimeOffset.UtcNow,
        null);

    private static ClassificationResult CreateClassification() =>
        // "raw" n'est plus une valeur de test valide pour ClassificationRawResponse : la colonne est
        // désormais un vrai jsonb, qui rejette tout ce qui n'est pas du JSON valide.
        new(null, [], RecommendedAction.ALire, Priority.Moyenne, "raison", "model", "{}");
}
