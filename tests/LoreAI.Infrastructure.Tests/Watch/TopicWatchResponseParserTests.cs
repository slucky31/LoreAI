using LoreAI.Infrastructure.Watch;

namespace LoreAI.Infrastructure.Tests.Watch;

public class TopicWatchResponseParserTests
{
    [Fact]
    public void TryParse_RelevantAndNew_ReturnsMatchingEvaluation()
    {
        var success = TopicWatchResponseParser.TryParse(
            """{ "isRelevant": true, "isNew": true, "tags": ["dotnet", "perf"], "reason": "Nouveau benchmark GC" }""",
            "model", "raw", out var result, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.True(result!.IsRelevant);
        Assert.True(result.IsNew);
        Assert.Equal(["dotnet", "perf"], result.Tags);
        Assert.Equal("Nouveau benchmark GC", result.Reason);
        Assert.False(result.IsFallback);
    }

    [Fact]
    public void TryParse_NotRelevant_ForcesIsNewFalseAndEmptyTags()
    {
        var success = TopicWatchResponseParser.TryParse(
            """{ "isRelevant": false, "isNew": true, "tags": ["dotnet"], "reason": "Hors sujet" }""",
            "model", "raw", out var result, out _);

        Assert.True(success);
        Assert.False(result!.IsRelevant);
        Assert.False(result.IsNew);
        Assert.Empty(result.Tags);
    }

    [Fact]
    public void TryParse_MissingIsRelevant_ReturnsError()
    {
        var success = TopicWatchResponseParser.TryParse(
            """{ "isNew": true, "tags": [], "reason": "x" }""",
            "model", "raw", out var result, out var error);

        Assert.False(success);
        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_MissingIsNew_ReturnsError()
    {
        var success = TopicWatchResponseParser.TryParse(
            """{ "isRelevant": true, "tags": [], "reason": "x" }""",
            "model", "raw", out var result, out var error);

        Assert.False(success);
        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_InvalidJson_ReturnsError()
    {
        var success = TopicWatchResponseParser.TryParse("not json", "model", "raw", out var result, out var error);

        Assert.False(success);
        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_MissingOptionalFields_DefaultsToEmptyTagsAndReason()
    {
        var success = TopicWatchResponseParser.TryParse(
            """{ "isRelevant": true, "isNew": true }""",
            "model", "raw", out var result, out _);

        Assert.True(success);
        Assert.Empty(result!.Tags);
        Assert.Equal(string.Empty, result.Reason);
    }

    [Fact]
    public void TryParse_DuplicateTags_DeduplicatesCaseInsensitively()
    {
        var success = TopicWatchResponseParser.TryParse(
            """{ "isRelevant": true, "isNew": true, "tags": ["dotnet", "DotNet", "perf"], "reason": "x" }""",
            "model", "raw", out var result, out _);

        Assert.True(success);
        Assert.Equal(2, result!.Tags.Count);
    }
}
