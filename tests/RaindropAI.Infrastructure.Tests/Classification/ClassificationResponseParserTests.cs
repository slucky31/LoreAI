using RaindropAI.Core.Enums;
using RaindropAI.Infrastructure.Classification;

namespace RaindropAI.Infrastructure.Tests.Classification;

public class ClassificationResponseParserTests
{
    private const string ValidJson = """
        { "suggestedCollection": ".NET", "tags": ["dotnet", "outil"], "action": "ATester", "priority": "Haute", "reason": "Nouvel outil .NET intéressant." }
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
        Assert.Equal("claude-haiku-4-5", result.Model);
        Assert.Equal("raw", result.RawResponse);
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
}
