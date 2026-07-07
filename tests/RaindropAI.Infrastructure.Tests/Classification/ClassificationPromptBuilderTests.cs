using RaindropAI.Core.Models;
using RaindropAI.Infrastructure.Classification;

namespace RaindropAI.Infrastructure.Tests.Classification;

public class ClassificationPromptBuilderTests
{
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

        var message = ClassificationPromptBuilder.BuildUserMessage(item);

        Assert.Contains(item.Title, message);
        Assert.Contains(item.Link, message);
        Assert.Contains("example.com", message);
        Assert.Contains("dotnet, claude", message);
        Assert.Contains("Un extrait court.", message);
        Assert.Contains("Ma note perso", message);
    }

    [Fact]
    public void BuildUserMessage_TruncatesLongExcerpt()
    {
        var longExcerpt = new string('a', 3000);
        var item = new RaindropItem(1, "Titre", "https://example.com", longExcerpt, null, [], null, null, null, DateTimeOffset.UtcNow, null);

        var message = ClassificationPromptBuilder.BuildUserMessage(item);

        Assert.DoesNotContain(new string('a', 2500), message);
        Assert.Contains('…', message);
    }

    [Fact]
    public void BuildUserMessage_HandlesMissingOptionalFields()
    {
        var item = new RaindropItem(1, "Titre", "https://example.com", null, null, [], null, null, null, DateTimeOffset.UtcNow, null);

        var message = ClassificationPromptBuilder.BuildUserMessage(item);

        Assert.Contains("(inconnu)", message);
        Assert.Contains("(aucun)", message);
        Assert.Contains("(aucun extrait)", message);
        Assert.Contains("(aucune)", message);
    }

    [Fact]
    public void BuildToolInputSchemaJson_IsValidJsonWithExpectedEnums()
    {
        var schema = ClassificationPromptBuilder.BuildToolInputSchemaJson();

        Assert.Contains("DotNet", schema);
        Assert.Contains("ATester", schema);
        Assert.Contains("Haute", schema);
    }
}
