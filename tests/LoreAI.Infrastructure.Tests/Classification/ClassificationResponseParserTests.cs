using LoreAI.Core.Enums;
using LoreAI.Infrastructure.Classification;

namespace LoreAI.Infrastructure.Tests.Classification;

public class ClassificationResponseParserTests
{
    private const string ValidJson = """
        { "suggestedCollection": ".NET", "tags": ["dotnet", "outil"], "action": "ATester", "priority": "Haute", "reason": "Nouvel outil .NET intéressant.", "summary": "Un outil qui simplifie le déploiement .NET sur ARM." }
        """;

    [Fact]
    public void Parse_ValidJson_ReturnsExpectedResult()
    {
        var result = ClassificationResponseParser.Parse(ValidJson, "claude-haiku-4-5", "raw");

        Assert.Equal(".NET", result.SuggestedCollection);
        Assert.Equal(["dotnet", "outil"], result.Tags);
        Assert.Equal(RecommendedAction.ATester, result.Action);
        Assert.Equal(Priority.Haute, result.Priority);
        Assert.Equal("Nouvel outil .NET intéressant.", result.Reason);
        Assert.Equal("Un outil qui simplifie le déploiement .NET sur ARM.", result.Summary);
        Assert.Equal("claude-haiku-4-5", result.Model);
        Assert.Equal("raw", result.RawResponse);
    }

    /// <summary>
    /// Traitement lenient, comme `reason` : un fixture pré-lot-4 (sans `summary`) doit continuer de parser,
    /// pas lever — le schéma d'outil le marque `required`, mais rien ne garantit que le modèle l'honore.
    /// </summary>
    [Fact]
    public void Parse_MissingSummary_DefaultsToEmptyString()
    {
        const string json = """{ "suggestedCollection": null, "tags": [], "action": "ATester", "priority": "Haute", "reason": "x" }""";

        var result = ClassificationResponseParser.Parse(json, "model", "raw");

        Assert.Equal(string.Empty, result.Summary);
    }

    [Fact]
    public void Parse_NullSuggestedCollection_ReturnsNull()
    {
        const string json = """{ "suggestedCollection": null, "tags": [], "action": "Reference", "priority": "Basse", "reason": "x" }""";

        var result = ClassificationResponseParser.Parse(json, "model", "raw");

        Assert.Null(result.SuggestedCollection);
    }

    [Fact]
    public void Parse_JsonWithCodeFences_StripsThemBeforeParsing()
    {
        var fenced = $"```json\n{ValidJson}\n```";

        var result = ClassificationResponseParser.Parse(fenced, "model", "raw");

        Assert.Equal(".NET", result.SuggestedCollection);
    }

    [Fact]
    public void Parse_CaseInsensitiveEnumValues_StillParses()
    {
        const string json = """{ "suggestedCollection": ".NET", "tags": [], "action": "atester", "priority": "haute", "reason": "ok" }""";

        var result = ClassificationResponseParser.Parse(json, "model", "raw");

        Assert.Equal(RecommendedAction.ATester, result.Action);
        Assert.Equal(Priority.Haute, result.Priority);
    }

    [Fact]
    public void Parse_InvalidEnumValue_ThrowsClassificationParseException()
    {
        const string json = """{ "suggestedCollection": null, "tags": [], "action": "PasUneAction", "priority": "Haute", "reason": "x" }""";

        Assert.Throws<ClassificationParseException>(() => ClassificationResponseParser.Parse(json, "model", "raw"));
    }

    [Fact]
    public void Parse_MissingField_ThrowsClassificationParseException()
    {
        const string json = """{ "suggestedCollection": null, "tags": [], "action": "ATester", "reason": "x" }""";

        Assert.Throws<ClassificationParseException>(() => ClassificationResponseParser.Parse(json, "model", "raw"));
    }

    [Fact]
    public void Parse_TagsNotAnArray_ThrowsClassificationParseException()
    {
        const string json = """{ "suggestedCollection": null, "tags": "dotnet", "action": "ATester", "priority": "Haute", "reason": "x" }""";

        Assert.Throws<ClassificationParseException>(() => ClassificationResponseParser.Parse(json, "model", "raw"));
    }

    [Fact]
    public void Parse_MalformedJson_ThrowsClassificationParseException()
    {
        const string malformed = "{ not json ";

        Assert.Throws<ClassificationParseException>(() => ClassificationResponseParser.Parse(malformed, "model", "raw"));
    }

    // --- F-11 : les tags sont la seule sortie libre du modèle écrite dans Raindrop ---

    [Fact]
    public void Parse_OverlyLongTag_IsTruncated()
    {
        var json = TagsJson($"\"{new string('a', 200)}\"");

        var tag = Assert.Single(ClassificationResponseParser.Parse(json, "model", "raw").Tags);
        Assert.Equal(50, tag.Length);
    }

    [Fact]
    public void Parse_TooManyTags_IsCapped()
    {
        var json = TagsJson(string.Join(", ", Enumerable.Range(1, 40).Select(i => $"\"tag{i}\"")));

        Assert.Equal(10, ClassificationResponseParser.Parse(json, "model", "raw").Tags.Count);
    }

    [Fact]
    public void Parse_TagWithNewlinesAndControlCharacters_IsFlattened()
    {
        var json = TagsJson("\"dot\\nnet\\u0007\"");

        var tag = Assert.Single(ClassificationResponseParser.Parse(json, "model", "raw").Tags);
        Assert.Equal("dot net", tag);
    }

    [Fact]
    public void Parse_DuplicateTags_AreCollapsedCaseInsensitively()
    {
        var json = TagsJson("\"dotnet\", \"DotNet\", \"  dotnet  \"");

        var tag = Assert.Single(ClassificationResponseParser.Parse(json, "model", "raw").Tags);
        Assert.Equal("dotnet", tag);
    }

    [Fact]
    public void Parse_BlankTags_AreDropped()
    {
        var json = TagsJson("\"dotnet\", \"\", \"   \", null");

        var tag = Assert.Single(ClassificationResponseParser.Parse(json, "model", "raw").Tags);
        Assert.Equal("dotnet", tag);
    }

    private static string TagsJson(string tags) =>
        $$"""{ "suggestedCollection": null, "tags": [{{tags}}], "action": "ATester", "priority": "Haute", "reason": "x" }""";
}
