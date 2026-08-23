using LoreAI.Core.Models;
using LoreAI.Core.Services;

namespace LoreAI.Core.Tests.Services;

public class TagHygieneAnalyzerTests
{
    [Fact]
    public void Analyze_SeparatorVariants_AreClustered()
    {
        var tags = new[] { new RaindropTag("dotnet", 10), new RaindropTag("dot-net", 3) };

        var result = TagHygieneAnalyzer.Analyze(tags);

        var cluster = Assert.Single(result.Clusters);
        Assert.Equal(["dot-net", "dotnet"], cluster.Tags);
    }

    [Fact]
    public void Analyze_UnrelatedTags_AreNotClustered()
    {
        var tags = new[] { new RaindropTag("dotnet", 10), new RaindropTag("kubernetes", 3) };

        var result = TagHygieneAnalyzer.Analyze(tags);

        Assert.Empty(result.Clusters);
    }

    [Fact]
    public void Analyze_TagUsedOnce_ReportedAsSingleUse()
    {
        var tags = new[] { new RaindropTag("dotnet", 10), new RaindropTag("obscure", 1) };

        var result = TagHygieneAnalyzer.Analyze(tags);

        Assert.Equal(["obscure"], result.SingleUseTags);
    }

    [Fact]
    public void Analyze_NoTags_ReturnsEmptyResult()
    {
        var result = TagHygieneAnalyzer.Analyze([]);

        Assert.Empty(result.Clusters);
        Assert.Empty(result.SingleUseTags);
    }
}
