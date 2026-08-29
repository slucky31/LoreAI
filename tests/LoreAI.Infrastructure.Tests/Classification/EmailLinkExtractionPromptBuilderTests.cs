using System.Text.Json;
using LoreAI.Infrastructure.Classification;

namespace LoreAI.Infrastructure.Tests.Classification;

public class EmailLinkExtractionPromptBuilderTests
{
    [Fact]
    public void BuildUserMessage_IncludesSubjectBodyAndCandidateUrls()
    {
        var message = EmailLinkExtractionPromptBuilder.BuildUserMessage(
            "Ma newsletter .NET",
            "Voici un article intéressant.",
            ["https://blog.example.com/article", "https://tools.example.com/cli"]);

        Assert.Contains("Ma newsletter .NET", message);
        Assert.Contains("Voici un article intéressant.", message);
        Assert.Contains("https://blog.example.com/article", message);
        Assert.Contains("https://tools.example.com/cli", message);
    }

    [Fact]
    public void BuildUserMessage_NoCandidateUrls_StillProducesAMessage()
    {
        var message = EmailLinkExtractionPromptBuilder.BuildUserMessage("Sujet", "Corps", []);

        Assert.Contains("(aucune)", message);
    }

    [Fact]
    public void BuildUserMessage_TruncatesLongBody()
    {
        var longBody = new string('a', 20_000);

        var message = EmailLinkExtractionPromptBuilder.BuildUserMessage("Sujet", longBody, []);

        Assert.True(message.Length < longBody.Length);
        Assert.Contains('…', message);
    }

    [Fact]
    public void BuildToolInputSchemaJson_ProducesValidJsonWithLinksArray()
    {
        var schemaJson = EmailLinkExtractionPromptBuilder.BuildToolInputSchemaJson();

        using var document = JsonDocument.Parse(schemaJson);
        var root = document.RootElement;

        Assert.Equal("object", root.GetProperty("type").GetString());
        Assert.True(root.GetProperty("properties").TryGetProperty("links", out _));
    }

    /// <summary>
    /// Régression pour la troncature observée le 2026-08-29 : le modèle désigne un lien par index, jamais en
    /// recopiant l'URL (coûteux en tokens pour des URLs de tracking longues) — vérifie que le schéma exige
    /// bien "index" et que le message présente les candidates numérotées.
    /// </summary>
    [Fact]
    public void BuildToolInputSchemaJson_LinksItemsRequireIndexNotUrl()
    {
        var schemaJson = EmailLinkExtractionPromptBuilder.BuildToolInputSchemaJson();

        using var document = JsonDocument.Parse(schemaJson);
        var itemProperties = document.RootElement
            .GetProperty("properties").GetProperty("links")
            .GetProperty("items").GetProperty("properties");

        Assert.True(itemProperties.TryGetProperty("index", out _));
        Assert.False(itemProperties.TryGetProperty("url", out _));
    }

    [Fact]
    public void BuildUserMessage_NumbersCandidateUrls()
    {
        var message = EmailLinkExtractionPromptBuilder.BuildUserMessage(
            "Sujet",
            "Corps",
            ["https://blog.example.com/article", "https://tools.example.com/cli"]);

        Assert.Contains("[0] https://blog.example.com/article", message);
        Assert.Contains("[1] https://tools.example.com/cli", message);
    }
}
