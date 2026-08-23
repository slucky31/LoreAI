using LoreAI.Core.Models;
using LoreAI.Core.Services;

namespace LoreAI.Core.Tests.Services;

public class DuplicateUrlDetectorTests
{
    [Fact]
    public void Detect_UtmParametersDiffer_StillGroupedAsDuplicates()
    {
        var items = new[]
        {
            CreateItem(1, "https://example.com/article?utm_source=newsletter"),
            CreateItem(2, "https://example.com/article?utm_source=twitter&utm_medium=social"),
        };

        var groups = DuplicateUrlDetector.Detect(items);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Items.Count);
    }

    [Fact]
    public void Detect_WwwPrefixAndTrailingSlashDiffer_StillGroupedAsDuplicates()
    {
        var items = new[]
        {
            CreateItem(1, "https://www.example.com/article/"),
            CreateItem(2, "https://example.com/article"),
        };

        var groups = DuplicateUrlDetector.Detect(items);

        Assert.Single(groups);
    }

    [Fact]
    public void Detect_FragmentDiffers_StillGroupedAsDuplicates()
    {
        var items = new[]
        {
            CreateItem(1, "https://example.com/article#section-2"),
            CreateItem(2, "https://example.com/article"),
        };

        var groups = DuplicateUrlDetector.Detect(items);

        Assert.Single(groups);
    }

    [Fact]
    public void Detect_NonUtmQueryParameterDiffers_NotGrouped()
    {
        var items = new[]
        {
            CreateItem(1, "https://example.com/article?page=1"),
            CreateItem(2, "https://example.com/article?page=2"),
        };

        var groups = DuplicateUrlDetector.Detect(items);

        Assert.Empty(groups);
    }

    [Fact]
    public void Detect_SingleOccurrence_NotReportedAsDuplicate()
    {
        var items = new[] { CreateItem(1, "https://example.com/unique") };

        Assert.Empty(DuplicateUrlDetector.Detect(items));
    }

    private static LibraryItemSummary CreateItem(long id, string url) =>
        new(id, $"Titre {id}", url, [], null, DateTimeOffset.UnixEpoch);
}
