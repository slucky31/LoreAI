using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RaindropAI.Core.Interfaces;
using RaindropAI.Core.Models;

namespace RaindropAI.Infrastructure.Classification;

/// <summary>
/// Pas de SDK .NET officiel Anthropic : consomme directement l'API Messages via HttpClient,
/// avec tool-use forcé pour garantir une sortie JSON strictement structurée.
/// </summary>
public sealed class AnthropicClassifier : IClassifier
{
    private const int MaxTokens = 300;

    private readonly HttpClient _httpClient;
    private readonly ClassifierOptions _options;
    private readonly ILogger<AnthropicClassifier> _logger;

    public AnthropicClassifier(HttpClient httpClient, IOptions<ClassifierOptions> options, ILogger<AnthropicClassifier> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _httpClient.BaseAddress ??= new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        if (!_httpClient.DefaultRequestHeaders.Contains("x-api-key"))
        {
            _httpClient.DefaultRequestHeaders.Add("x-api-key", _options.ApiKey);
        }
        if (!_httpClient.DefaultRequestHeaders.Contains("anthropic-version"))
        {
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", _options.AnthropicVersion);
        }
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<ClassificationResult> ClassifyAsync(RaindropItem item, CancellationToken cancellationToken)
    {
        var requestBody = BuildRequestBody(item);
        var rawResponseBody = string.Empty;

        try
        {
            using var response = await _httpClient.PostAsJsonAsync("v1/messages", requestBody, cancellationToken);
            rawResponseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();

            var toolInputJson = ExtractToolInput(rawResponseBody);
            return ClassificationResponseParser.Parse(toolInputJson, _options.Model, rawResponseBody);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Classification échouée pour le raindrop {RaindropId}", item.Id);
            return ClassificationResult.Fallback(_options.Model, $"Classification échouée: {ex.Message}", rawResponseBody);
        }
    }

    private object BuildRequestBody(RaindropItem item) => new
    {
        model = _options.Model,
        max_tokens = MaxTokens,
        system = ClassificationPromptBuilder.SystemPrompt,
        messages = new[]
        {
            new { role = "user", content = ClassificationPromptBuilder.BuildUserMessage(item) }
        },
        tools = new[]
        {
            new
            {
                name = ClassificationPromptBuilder.ToolName,
                description = "Classe un article Raindrop selon sa catégorie, l'action recommandée et sa priorité.",
                input_schema = JsonSerializer.Deserialize<JsonElement>(ClassificationPromptBuilder.BuildToolInputSchemaJson())
            }
        },
        tool_choice = new { type = "tool", name = ClassificationPromptBuilder.ToolName }
    };

    private static string ExtractToolInput(string rawResponseBody)
    {
        using var document = JsonDocument.Parse(rawResponseBody);
        foreach (var block in document.RootElement.GetProperty("content").EnumerateArray())
        {
            if (block.TryGetProperty("type", out var typeElement) && typeElement.GetString() == "tool_use")
            {
                return block.GetProperty("input").GetRawText();
            }
        }

        throw new ClassificationParseException("Aucun bloc tool_use trouvé dans la réponse Anthropic.");
    }
}
