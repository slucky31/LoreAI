using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using RaindropAI.Core.Enums;
using RaindropAI.Core.Models;
using RaindropAI.Infrastructure.Persistence;

namespace RaindropAI.Infrastructure.Tests.Persistence;

public class ArticleRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ArticleRepository _repository;

    public ArticleRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"raindropai-test-{Guid.NewGuid():N}.db");
        var factory = new SqliteConnectionFactory(Options.Create(new SqliteOptions { ConnectionString = $"Data Source={_dbPath}" }));
        _repository = new ArticleRepository(factory);
    }

    [Fact]
    public async Task UpsertAsync_CalledTwiceWithSameId_DoesNotDuplicate()
    {
        var item = CreateItem(1, "Titre initial");
        var classification = CreateClassification();

        await _repository.UpsertAsync(item, classification, DateTimeOffset.UtcNow, CancellationToken.None);
        await _repository.UpsertAsync(item with { Title = "Titre mis à jour" }, classification, DateTimeOffset.UtcNow, CancellationToken.None);

        var pending = await _repository.GetUnsentDigestItemsAsync(CancellationToken.None);

        var single = Assert.Single(pending);
        Assert.Equal("Titre mis à jour", single.Item.Title);
    }

    [Fact]
    public async Task GetUnsentDigestItemsAsync_ExcludesArticlesAlreadySent()
    {
        await _repository.UpsertAsync(CreateItem(1, "A"), CreateClassification(), DateTimeOffset.UtcNow, CancellationToken.None);
        await _repository.UpsertAsync(CreateItem(2, "B"), CreateClassification(), DateTimeOffset.UtcNow, CancellationToken.None);

        await _repository.MarkDigestSentAsync([1], DateTimeOffset.UtcNow, CancellationToken.None);

        var pending = await _repository.GetUnsentDigestItemsAsync(CancellationToken.None);

        var single = Assert.Single(pending);
        Assert.Equal(2, single.Item.Id);
    }

    [Fact]
    public async Task MarkDiscordNotifiedAsync_SetsTimestamp()
    {
        await _repository.UpsertAsync(CreateItem(1, "A"), CreateClassification(), DateTimeOffset.UtcNow, CancellationToken.None);

        await _repository.MarkDiscordNotifiedAsync(1, DateTimeOffset.UtcNow, CancellationToken.None);

        var pending = await _repository.GetUnsentDigestItemsAsync(CancellationToken.None);
        var single = Assert.Single(pending);
        Assert.NotNull(single.DiscordNotifiedAtUtc);
    }

    [Fact]
    public async Task RecordWriteBackAsync_Moved_SetsMovedFlag()
    {
        await _repository.UpsertAsync(CreateItem(1, "A"), CreateClassification(), DateTimeOffset.UtcNow, CancellationToken.None);

        await _repository.RecordWriteBackAsync(1, success: true, moved: true, DateTimeOffset.UtcNow, CancellationToken.None);

        var pending = await _repository.GetUnsentDigestItemsAsync(CancellationToken.None);
        var single = Assert.Single(pending);
        Assert.True(single.Moved);
    }

    [Fact]
    public async Task RecordWriteBackAsync_TagsOnly_LeavesMovedFalse()
    {
        await _repository.UpsertAsync(CreateItem(1, "A"), CreateClassification(), DateTimeOffset.UtcNow, CancellationToken.None);

        await _repository.RecordWriteBackAsync(1, success: true, moved: false, DateTimeOffset.UtcNow, CancellationToken.None);

        var pending = await _repository.GetUnsentDigestItemsAsync(CancellationToken.None);
        var single = Assert.Single(pending);
        Assert.False(single.Moved);
    }

    [Fact]
    public async Task UpsertAsync_RoundTripsTagsAndSuggestedCollection()
    {
        var item = CreateItem(1, "A") with { Tags = ["dotnet", "claude"] };
        var classification = new ClassificationResult("Claude", ["claude", "ia"], RecommendedAction.ATester, Priority.Haute, "raison", "claude-haiku-4-5", "{}");

        await _repository.UpsertAsync(item, classification, DateTimeOffset.UtcNow, CancellationToken.None);

        var pending = await _repository.GetUnsentDigestItemsAsync(CancellationToken.None);
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

        await _repository.UpsertAsync(item, classification, DateTimeOffset.UtcNow, CancellationToken.None);

        var pending = await _repository.GetUnsentDigestItemsAsync(CancellationToken.None);
        var single = Assert.Single(pending);

        Assert.Null(single.Classification.SuggestedCollection);
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
        new(null, [], RecommendedAction.ALire, Priority.Moyenne, "raison", "model", "raw");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
