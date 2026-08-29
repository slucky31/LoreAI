using LoreAI.Infrastructure.Watch;

namespace LoreAI.Infrastructure.Tests.Watch;

public class TopicWatchResponseParserTests
{
    [Fact]
    public void TryParse_RelevantAndNew_ReturnsMatchingEvaluation()
    {
        var success = TopicWatchResponseParser.TryParse(
            """{ "isRelevant": true, "matchedTopic": "dotnet-perf", "isNew": true, "reason": "Nouveau benchmark GC" }""",
            "model", "raw", out var result, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.True(result!.IsRelevant);
        Assert.True(result.IsNew);
        Assert.Equal("dotnet-perf", result.MatchedTopic);
        Assert.Equal("Nouveau benchmark GC", result.Reason);
        Assert.False(result.IsFallback);
    }

    [Fact]
    public void TryParse_NotRelevant_ForcesIsNewFalseAndMatchedTopicNull()
    {
        var success = TopicWatchResponseParser.TryParse(
            """{ "isRelevant": false, "matchedTopic": "dotnet-perf", "isNew": true, "reason": "Hors sujet" }""",
            "model", "raw", out var result, out _);

        Assert.True(success);
        Assert.False(result!.IsRelevant);
        Assert.False(result.IsNew);
        Assert.Null(result.MatchedTopic);
    }

    [Fact]
    public void TryParse_MissingIsRelevant_ReturnsError()
    {
        var success = TopicWatchResponseParser.TryParse(
            """{ "isNew": true, "reason": "x" }""",
            "model", "raw", out var result, out var error);

        Assert.False(success);
        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_MissingIsNew_ReturnsError()
    {
        var success = TopicWatchResponseParser.TryParse(
            """{ "isRelevant": true, "reason": "x" }""",
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
    public void TryParse_MissingOptionalFields_DefaultsToNullAndEmptyReason()
    {
        var success = TopicWatchResponseParser.TryParse(
            """{ "isRelevant": true, "isNew": true }""",
            "model", "raw", out var result, out _);

        Assert.True(success);
        Assert.Null(result!.MatchedTopic);
        Assert.Equal(string.Empty, result.Reason);
    }
}
