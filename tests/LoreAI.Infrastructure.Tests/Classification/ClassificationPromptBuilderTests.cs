using LoreAI.Core.Models;
using LoreAI.Infrastructure.Classification;

namespace LoreAI.Infrastructure.Tests.Classification;

public class ClassificationPromptBuilderTests
{
    private static readonly RaindropTaxonomy SampleTaxonomy = new(
        [new RaindropCollection(1, ".NET"), new RaindropCollection(2, "Formations")],
        [new RaindropTag("dotnet", 10), new RaindropTag("claude", 5)]);

    [Fact]
    public void BuildUserMessage_IncludesAllRelevantFields()
    {
        var item = new RaindropItem(
            Id: 1,
            Title: "Un article .NET",
            Link: "https://example.com/article",
            Excerpt: "Un extrait court.",
            Note: "Ma note perso",
            Tags: ["dotnet", "claude"],
            CollectionId: 42,
            Domain: "example.com",
            RaindropType: "article",
            CreatedUtc: DateTimeOffset.UtcNow,
            LastUpdateUtc: null);

        var message = ClassificationPromptBuilder.BuildUserMessage(item, SampleTaxonomy);

        Assert.Contains(item.Title, message);
        Assert.Contains(item.Link, message);
        Assert.Contains("example.com", message);
        Assert.Contains("dotnet, claude", message);
        Assert.Contains("Un extrait court.", message);
        Assert.Contains("Ma note perso", message);
        Assert.Contains(".NET", message);
        Assert.Contains("Formations", message);
    }

    [Fact]
    public void BuildUserMessage_TruncatesLongExcerpt()
    {
        var longExcerpt = new string('a', 3000);
        var item = new RaindropItem(1, "Titre", "https://example.com", longExcerpt, null, [], null, null, null, DateTimeOffset.UtcNow, null);

        var message = ClassificationPromptBuilder.BuildUserMessage(item, SampleTaxonomy);

        Assert.DoesNotContain(new string('a', 2500), message);
        Assert.Contains('…', message);
    }

    [Fact]
    public void BuildUserMessage_HandlesMissingOptionalFields()
    {
        var item = new RaindropItem(1, "Titre", "https://example.com", null, null, [], null, null, null, DateTimeOffset.UtcNow, null);
        var emptyTaxonomy = new RaindropTaxonomy([], []);

        var message = ClassificationPromptBuilder.BuildUserMessage(item, emptyTaxonomy);

        Assert.Contains("(inconnu)", message);
        Assert.Contains("(aucun)", message);
        Assert.Contains("(aucun extrait)", message);
        Assert.Contains("(aucune)", message);
        Assert.Contains("(aucune collection existante)", message);
        Assert.Contains("(aucun tag existant)", message);
    }

    [Fact]
    public void BuildUserMessage_OrdersTagsByPopularity()
    {
        var item = new RaindropItem(1, "Titre", "https://example.com", null, null, [], null, null, null, DateTimeOffset.UtcNow, null);
        var taxonomy = new RaindropTaxonomy([], [new RaindropTag("rare", 1), new RaindropTag("populaire", 100)]);

        var message = ClassificationPromptBuilder.BuildUserMessage(item, taxonomy);

        Assert.True(message.IndexOf("populaire", StringComparison.Ordinal) < message.IndexOf("rare", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildToolInputSchemaJson_ListsExistingCollectionsAndFixedEnums()
    {
        var schema = ClassificationPromptBuilder.BuildToolInputSchemaJson(SampleTaxonomy);

        Assert.Contains(".NET", schema);
        Assert.Contains("Formations", schema);
        Assert.Contains("ATester", schema);
        Assert.Contains("Haute", schema);
    }
}
