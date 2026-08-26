using LoreAI.Infrastructure.Classification;

namespace LoreAI.Infrastructure.Tests.Classification;

public class EmailLinkExtractionResponseParserTests
{
    private static readonly string[] CandidateUrls =
    [
        "https://blog.example.com/real-article",
        "https://tools.example.com/new-cli",
    ];

    [Fact]
    public void Parse_ValidJson_ReturnsExpectedLinks()
    {
        const string json = """
            { "links": [
                { "url": "https://blog.example.com/real-article", "title": "Un vrai article .NET" },
                { "url": "https://tools.example.com/new-cli", "title": "Un nouvel outil CLI" }
            ] }
            """;

        var result = EmailLinkExtractionResponseParser.Parse(json, CandidateUrls);

        Assert.Equal(2, result.Count);
        Assert.Equal("https://blog.example.com/real-article", result[0].Url);
        Assert.Equal("Un vrai article .NET", result[0].Title);
    }

    [Fact]
    public void Parse_EmptyLinksArray_ReturnsEmptyList()
    {
        const string json = """{ "links": [] }""";

        var result = EmailLinkExtractionResponseParser.Parse(json, CandidateUrls);

        Assert.Empty(result);
    }

    /// <summary>
    /// Garde de sécurité propre à ce parseur (cf. F-11) : le modèle ne doit jamais pouvoir faire écrire
    /// une URL qu'il aurait inventée ou légèrement modifiée par rapport aux URLs candidates fournies.
    /// </summary>
    [Fact]
    public void Parse_UrlNotInCandidateList_IsDropped()
    {
        const string json = """
            { "links": [
                { "url": "https://blog.example.com/real-article", "title": "Un vrai article" },
                { "url": "https://evil.example.com/not-a-candidate", "title": "Inventé" }
            ] }
            """;

        var result = EmailLinkExtractionResponseParser.Parse(json, CandidateUrls);

        var single = Assert.Single(result);
        Assert.Equal("https://blog.example.com/real-article", single.Url);
    }

    [Fact]
    public void Parse_DuplicateUrls_KeepsOnlyFirstOccurrence()
    {
        const string json = """
            { "links": [
                { "url": "https://blog.example.com/real-article", "title": "Titre 1" },
                { "url": "https://blog.example.com/real-article", "title": "Titre 2" }
            ] }
            """;

        var result = EmailLinkExtractionResponseParser.Parse(json, CandidateUrls);

        var single = Assert.Single(result);
        Assert.Equal("Titre 1", single.Title);
    }

    [Fact]
    public void Parse_MissingTitle_FallsBackToUrl()
    {
        const string json = """{ "links": [ { "url": "https://blog.example.com/real-article" } ] }""";

        var result = EmailLinkExtractionResponseParser.Parse(json, CandidateUrls);

        var single = Assert.Single(result);
        Assert.Equal("https://blog.example.com/real-article", single.Title);
    }

    [Fact]
    public void Parse_MissingLinksField_Throws()
    {
        const string json = """{ "notLinks": [] }""";

        Assert.Throws<EmailLinkExtractionParseException>(() => EmailLinkExtractionResponseParser.Parse(json, CandidateUrls));
    }

    [Fact]
    public void TryParse_InvalidJson_ReturnsFalse()
    {
        var success = EmailLinkExtractionResponseParser.TryParse("not json", CandidateUrls, out var result, out var error);

        Assert.False(success);
        Assert.Null(result);
        Assert.NotNull(error);
    }

    /// <summary>Tolère les fences ```json résiduels, même patron que ClassificationResponseParser.</summary>
    [Fact]
    public void Parse_WrappedInCodeFences_StillParses()
    {
        const string json = "```json\n{ \"links\": [] }\n```";

        var result = EmailLinkExtractionResponseParser.Parse(json, CandidateUrls);

        Assert.Empty(result);
    }
}
