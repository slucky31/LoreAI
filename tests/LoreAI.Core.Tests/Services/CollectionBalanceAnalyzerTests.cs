using LoreAI.Core.Models;
using LoreAI.Core.Services;

namespace LoreAI.Core.Tests.Services;

public class CollectionBalanceAnalyzerTests
{
    [Fact]
    public void Detect_CollectionWithOneItem_IsReported()
    {
        var items = new[] { CreateItem(1, collectionId: 10) };
        var titles = new Dictionary<long, string> { [10] = "Veille" };

        var result = CollectionBalanceAnalyzer.Detect(items, titles);

        var single = Assert.Single(result);
        Assert.Equal("Veille", single.Title);
        Assert.Equal(1, single.ItemCount);
    }

    [Fact]
    public void Detect_CollectionWithThreeItems_NotReported()
    {
        var items = new[] { CreateItem(1, 10), CreateItem(2, 10), CreateItem(3, 10) };
        var titles = new Dictionary<long, string> { [10] = "Veille" };

        Assert.Empty(CollectionBalanceAnalyzer.Detect(items, titles));
    }

    [Fact]
    public void Detect_UnsortedCollectionId_IsIgnored()
    {
        var items = new[] { CreateItem(1, collectionId: -1) };
        var titles = new Dictionary<long, string>();

        Assert.Empty(CollectionBalanceAnalyzer.Detect(items, titles));
    }

    [Fact]
    public void Detect_NullCollectionId_IsIgnored()
    {
        var items = new[] { CreateItem(1, collectionId: null) };
        var titles = new Dictionary<long, string>();

        Assert.Empty(CollectionBalanceAnalyzer.Detect(items, titles));
    }

    private static LibraryItemSummary CreateItem(long id, long? collectionId) =>
        new(id, $"Titre {id}", $"https://example.com/{id}", [], collectionId, DateTimeOffset.UnixEpoch);
}
