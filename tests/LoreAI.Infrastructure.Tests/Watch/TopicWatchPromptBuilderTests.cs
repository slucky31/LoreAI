using LoreAI.Core.Enums;
using LoreAI.Core.Models;
using LoreAI.Infrastructure.Watch;

namespace LoreAI.Infrastructure.Tests.Watch;

public class TopicWatchPromptBuilderTests
{
    private static readonly Item Candidate = new(
        SourceType.Watch, "1", "https://blog.example.com/article", "Un article", null, null, [], DateTimeOffset.UtcNow);

    [Fact]
    public void BuildUserMessage_ListsConfiguredTopics()
    {
        var topics = new[] { new WatchTopic("dotnet-perf", "Optimisations de performance .NET") };

        var message = TopicWatchPromptBuilder.BuildUserMessage(Candidate, topics, []);

        Assert.Contains("dotnet-perf", message, StringComparison.Ordinal);
        Assert.Contains("Optimisations de performance .NET", message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildUserMessage_NoTopics_StatesNoneConfigured()
    {
        var message = TopicWatchPromptBuilder.BuildUserMessage(Candidate, [], []);

        Assert.Contains("(aucun sujet configuré)", message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildUserMessage_NoRelatedItems_StatesNoneKnown()
    {
        var message = TopicWatchPromptBuilder.BuildUserMessage(Candidate, [], []);

        Assert.Contains("(aucun article déjà connu sur un sujet proche)", message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildUserMessage_RelatedItems_ListsTheirTitles()
    {
        var related = new[] { new LibraryItemSummary(1, "Article déjà connu", "https://example.com", [], null, DateTimeOffset.UtcNow) };

        var message = TopicWatchPromptBuilder.BuildUserMessage(Candidate, [], related);

        Assert.Contains("Article déjà connu", message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildUserMessage_WrapsCandidateInDelimitedBlock()
    {
        var message = TopicWatchPromptBuilder.BuildUserMessage(Candidate, [], []);

        Assert.Contains("<candidate>", message, StringComparison.Ordinal);
        Assert.Contains("</candidate>", message, StringComparison.Ordinal);
        Assert.Contains(Candidate.Title, message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildToolInputSchemaJson_IsValidJson()
    {
        var json = TopicWatchPromptBuilder.BuildToolInputSchemaJson();

        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("object", document.RootElement.GetProperty("type").GetString());
    }
}
