using LoreAI.Core.Models;
using LoreAI.Core.Services;

namespace LoreAI.Core.Tests.Services;

public class TrendAnalyzerTests
{
    [Fact]
    public void TopDomains_CountsByHostWithoutWwwPrefix()
    {
        var items = new[]
        {
            CreateItem("https://www.github.com/a"),
            CreateItem("https://github.com/b"),
            CreateItem("https://example.com/c"),
        };

        var domains = TrendAnalyzer.TopDomains(items);

        Assert.Equal(2, domains.Single(d => d.Domain == "github.com").Count);
    }

    [Fact]
    public void TopTags_CountsAcrossItemsCaseInsensitively()
    {
        var items = new[]
        {
            CreateItem("https://a.example", "dotnet"),
            CreateItem("https://b.example", "DotNet"),
            CreateItem("https://c.example", "claude"),
        };

        var tags = TrendAnalyzer.TopTags(items);

        Assert.Equal(2, tags.Single(t => t.Tag.Equals("dotnet", StringComparison.OrdinalIgnoreCase)).Count);
    }

    [Fact]
    public void TopDomains_RespectsTopLimit()
    {
        var items = Enumerable.Range(1, 5).Select(i => CreateItem($"https://domain{i}.example")).ToArray();

        var domains = TrendAnalyzer.TopDomains(items, top: 2);

        Assert.Equal(2, domains.Count);
    }

    private static LibraryItemSummary CreateItem(string url, params string[] tags) =>
        new(1, "Titre", url, tags, null, DateTimeOffset.UnixEpoch);
}
