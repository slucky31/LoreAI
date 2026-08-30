using LoreAI.Core.Enums;
using LoreAI.Core.Models;
using LoreAI.Infrastructure.Watch;

namespace LoreAI.Infrastructure.Tests.Watch;

public class TopicWatchPromptBuilderTests
{
    private static readonly Item Candidate = new(
        SourceType.Watch, "1", "https://blog.example.com/article", "Un article", null, null, [], DateTimeOffset.UtcNow);

    private static readonly WatchTopic Topic = new(1, "dotnet-perf", "Optimisations de performance .NET", 7, 42, "0", DateTimeOffset.UtcNow);

    private static readonly RaindropTaxonomy EmptyTaxonomy = new([], []);

    [Fact]
    public void BuildUserMessage_MentionsTopicNameAndDescription()
    {
        var message = TopicWatchPromptBuilder.BuildUserMessage(Candidate, Topic, EmptyTaxonomy, []);

        Assert.Contains("dotnet-perf", message, StringComparison.Ordinal);
        Assert.Contains("Optimisations de performance .NET", message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildUserMessage_NoRelatedItems_StatesNoneKnown()
    {
        var message = TopicWatchPromptBuilder.BuildUserMessage(Candidate, Topic, EmptyTaxonomy, []);

        Assert.Contains("(aucun article déjà connu sur un sujet proche)", message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildUserMessage_RelatedItems_ListsTheirTitles()
    {
        var related = new[] { new LibraryItemSummary(1, "Article déjà connu", "https://example.com", [], null, DateTimeOffset.UtcNow) };

        var message = TopicWatchPromptBuilder.BuildUserMessage(Candidate, Topic, EmptyTaxonomy, related);

        Assert.Contains("Article déjà connu", message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildUserMessage_ExistingTags_ListedForReuse()
    {
        var taxonomy = new RaindropTaxonomy([], [new RaindropTag("dotnet", 12)]);

        var message = TopicWatchPromptBuilder.BuildUserMessage(Candidate, Topic, taxonomy, []);

        Assert.Contains("dotnet", message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildUserMessage_WrapsCandidateInDelimitedBlock()
    {
        var message = TopicWatchPromptBuilder.BuildUserMessage(Candidate, Topic, EmptyTaxonomy, []);

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
