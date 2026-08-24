using LoreAI.Core.Enums;
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
        var item = new Item(
            SourceType: SourceType.Raindrop,
            SourceId: "1",
            Url: "https://example.com/article",
            Title: "Un article .NET",
            Excerpt: "Un extrait court.",
            Note: "Ma note perso",
            Tags: ["dotnet", "claude"],
            CapturedAtUtc: DateTimeOffset.UtcNow);

        var message = ClassificationPromptBuilder.BuildUserMessage(item, SampleTaxonomy);

        Assert.Contains(item.Title, message);
        Assert.Contains(item.Url, message);
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
        var item = new Item(SourceType.Raindrop, "1", "https://example.com", "Titre", longExcerpt, null, [], DateTimeOffset.UtcNow);

        var message = ClassificationPromptBuilder.BuildUserMessage(item, SampleTaxonomy);

        Assert.DoesNotContain(new string('a', 2500), message);
        Assert.Contains('…', message);
    }

    [Fact]
    public void BuildUserMessage_HandlesMissingOptionalFields()
    {
        // Url volontairement invalide : c'est le seul cas où le domaine (désormais recalculé, ADR 0012)
        // ne peut pas être déterminé.
        var item = new Item(SourceType.Raindrop, "1", "url-invalide", "Titre", null, null, [], DateTimeOffset.UtcNow);
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
        var item = new Item(SourceType.Raindrop, "1", "https://example.com", "Titre", null, null, [], DateTimeOffset.UtcNow);
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

    [Fact]
    public void BuildToolInputSchemaJson_IncludesRequiredSummaryField()
    {
        var schema = ClassificationPromptBuilder.BuildToolInputSchemaJson(SampleTaxonomy);

        Assert.Contains("\"summary\"", schema);
        Assert.Contains("\"required\":[\"suggestedCollection\",\"tags\",\"action\",\"priority\",\"reason\",\"summary\"]", schema);
    }

    /// <summary>S1 (lot 4) : le contenu réel remplace l'excerpt dans le prompt quand il est disponible.</summary>
    [Fact]
    public void BuildUserMessage_ContentTextProvided_UsesContentInsteadOfExcerpt()
    {
        var item = new Item(SourceType.Raindrop, "1", "https://example.com", "Titre", "Excerpt Raindrop", null, [], DateTimeOffset.UtcNow);

        var message = ClassificationPromptBuilder.BuildUserMessage(item, SampleTaxonomy, "Contenu réel de la page");

        Assert.Contains("Contenu réel de la page", message);
        Assert.DoesNotContain("Excerpt Raindrop", message);
    }

    [Fact]
    public void BuildUserMessage_ContentTextTooLong_IsTruncated()
    {
        var item = new Item(SourceType.Raindrop, "1", "https://example.com", "Titre", null, null, [], DateTimeOffset.UtcNow);
        var longContent = new string('b', 7000);

        var message = ClassificationPromptBuilder.BuildUserMessage(item, SampleTaxonomy, longContent);

        Assert.DoesNotContain(new string('b', 6500), message);
        Assert.Contains('…', message);
    }

    [Fact]
    public void BuildUserMessage_ContentTextAbsent_FallsBackToExcerpt()
    {
        var item = new Item(SourceType.Raindrop, "1", "https://example.com", "Titre", "Excerpt Raindrop", null, [], DateTimeOffset.UtcNow);

        var message = ClassificationPromptBuilder.BuildUserMessage(item, SampleTaxonomy, contentText: null);

        Assert.Contains("Excerpt Raindrop", message);
    }
}
