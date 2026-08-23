using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;

namespace LoreAI.Infrastructure.Classification;

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

    public async Task<ClassificationResult> ClassifyAsync(Item item, RaindropTaxonomy taxonomy, CancellationToken cancellationToken)
    {
        var requestBody = BuildRequestBody(item, taxonomy);
        var rawResponseBody = string.Empty;

        try
        {
            using var response = await _httpClient.PostAsJsonAsync("v1/messages", requestBody, cancellationToken);
            rawResponseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();

            if (!TryExtractToolInput(rawResponseBody, out var toolInputJson, out var extractError))
            {
                return Fallback(item, extractError, rawResponseBody);
            }

            return ClassificationResponseParser.TryParse(toolInputJson, _options.Model, rawResponseBody, out var result, out var parseError)
                ? result
                : Fallback(item, parseError, rawResponseBody);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Classification échouée pour l'item {SourceId}", item.SourceId);
            return ClassificationResult.Fallback(_options.Model, $"Classification échouée: {ex.Message}", rawResponseBody);
        }
    }

    /// <summary>
    /// Seules les pannes de transport et de format donnent lieu à un repli. Tout le reste — typiquement un
    /// bug de ce code — remonte à l'appelant, qui le journalisera en erreur avec sa pile plutôt que de le
    /// déguiser en « classification échouée ».
    /// </summary>
    private static bool IsTransportFailure(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        // Arrêt de l'application demandé : ce n'est pas un échec, il doit remonter tel quel (cf. F-06).
        OperationCanceledException when cancellationToken.IsCancellationRequested => false,
        // Délai dépassé côté handler de résilience : matérialisé par une annulation non demandée.
        OperationCanceledException => true,
        HttpRequestException or JsonException or TimeoutException => true,
        _ => false,
    };

    private ClassificationResult Fallback(Item item, string reason, string rawResponseBody)
    {
        _logger.LogWarning("Classification inexploitable pour l'item {SourceId} : {Reason}", item.SourceId, reason);
        return ClassificationResult.Fallback(_options.Model, $"Classification échouée: {reason}", rawResponseBody);
    }

    private object BuildRequestBody(Item item, RaindropTaxonomy taxonomy) => new
    {
        model = _options.Model,
        max_tokens = MaxTokens,
        system = ClassificationPromptBuilder.SystemPrompt,
        messages = new[]
        {
            new { role = "user", content = ClassificationPromptBuilder.BuildUserMessage(item, taxonomy) }
        },
        tools = new[]
        {
            new
            {
                name = ClassificationPromptBuilder.ToolName,
                description = "Classe un article Raindrop \"Non trié\" : collection existante correspondante (ou aucune), tags, action recommandée, priorité.",
                input_schema = JsonSerializer.Deserialize<JsonElement>(ClassificationPromptBuilder.BuildToolInputSchemaJson(taxonomy))
            }
        },
        tool_choice = new { type = "tool", name = ClassificationPromptBuilder.ToolName }
    };

    private static bool TryExtractToolInput(
        string rawResponseBody,
        [NotNullWhen(true)] out string? toolInputJson,
        [NotNullWhen(false)] out string? error)
    {
        toolInputJson = null;

        using var document = JsonDocument.Parse(rawResponseBody);
        var root = document.RootElement;

        // Une réponse tronquée porte un bloc tool_use au JSON incomplet : le signaler explicitement
        // vaut mieux que de laisser le parser conclure sur des champs partiels.
        if (root.TryGetProperty("stop_reason", out var stopReason) && stopReason.GetString() == "max_tokens")
        {
            error = $"réponse Anthropic tronquée (stop_reason=max_tokens, max_tokens={MaxTokens})";
            return false;
        }

        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            error = "réponse Anthropic sans tableau content";
            return false;
        }

        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var typeElement)
                && typeElement.GetString() == "tool_use"
                && block.TryGetProperty("input", out var input))
            {
                toolInputJson = input.GetRawText();
                error = null;
                return true;
            }
        }

        error = "aucun bloc tool_use dans la réponse Anthropic";
        return false;
    }
}
