using LoreAI.Core.Services;

namespace LoreAI.Core.Tests.Services;

public class LlmUsageAnalyzerTests
{
    [Fact]
    public void Analyze_ValidUsage_SumsTokensAcrossResponses()
    {
        var responses = new[]
        {
            BuildResponse(input: 3000, output: 300, cacheCreation: 0, cacheRead: 0),
            BuildResponse(input: 2000, output: 200, cacheCreation: 100, cacheRead: 50),
        };

        var summary = LlmUsageAnalyzer.Analyze(responses);

        Assert.Equal(2, summary.ClassificationCount);
        Assert.Equal(5000, summary.InputTokens);
        Assert.Equal(500, summary.OutputTokens);
        Assert.Equal(100, summary.CacheCreationInputTokens);
        Assert.Equal(50, summary.CacheReadInputTokens);
    }

    [Fact]
    public void Analyze_EstimatesCostAtHaikuRates()
    {
        // 1 000 000 tokens entrée (1 $) + 1 000 000 tokens sortie (5 $) = 6 $.
        var responses = new[] { BuildResponse(input: 1_000_000, output: 1_000_000, cacheCreation: 0, cacheRead: 0) };

        var summary = LlmUsageAnalyzer.Analyze(responses);

        Assert.Equal(6m, summary.EstimatedCostUsd);
    }

    [Fact]
    public void Analyze_MalformedJson_ContributesZeroWithoutThrowing()
    {
        var responses = new[] { "not json at all" };

        var summary = LlmUsageAnalyzer.Analyze(responses);

        Assert.Equal(0, summary.ClassificationCount);
        Assert.Equal(0, summary.InputTokens);
    }

    [Fact]
    public void Analyze_EmptyString_ContributesZeroWithoutThrowing()
    {
        var summary = LlmUsageAnalyzer.Analyze([string.Empty]);

        Assert.Equal(0, summary.ClassificationCount);
    }

    /// <summary>
    /// Régression : <c>ArticleRepository.NormalizeToJson</c> enveloppe un corps non-JSON dans une chaîne
    /// JSON valide (ex. <c>"\"boom\""</c>) — la racine devient un <c>JsonValueKind.String</c>, pas un objet.
    /// </summary>
    [Fact]
    public void Analyze_JsonStringRoot_ContributesZeroWithoutThrowing()
    {
        var summary = LlmUsageAnalyzer.Analyze(["\"boom\""]);

        Assert.Equal(0, summary.ClassificationCount);
    }

    [Fact]
    public void Analyze_ResponseWithoutUsageObject_ContributesZeroWithoutThrowing()
    {
        var summary = LlmUsageAnalyzer.Analyze(["{\"id\":\"msg_1\"}"]);

        Assert.Equal(0, summary.ClassificationCount);
    }

    private static string BuildResponse(int input, int output, int cacheCreation, int cacheRead) =>
        $$"""
        {
          "id": "msg_1",
          "usage": {
            "input_tokens": {{input}},
            "output_tokens": {{output}},
            "cache_creation_input_tokens": {{cacheCreation}},
            "cache_read_input_tokens": {{cacheRead}}
          }
        }
        """;
}
